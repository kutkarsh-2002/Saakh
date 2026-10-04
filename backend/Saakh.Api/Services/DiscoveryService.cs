using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Mapping;

namespace Saakh.Api.Services;

/// <summary>Multi-select discovery filters (spec, Dashboard/Discovery/Search).</summary>
public class DiscoveryQuery
{
    public string? Search { get; init; }

    public string? Country { get; init; }

    /// <summary>Multi-select: any of these states matches.</summary>
    public string[]? States { get; init; }

    public string[]? Districts { get; init; }

    public DealCategory[]? Categories { get; init; }

    public int[]? SubTypeIds { get; init; }

    public decimal? CapacityMin { get; init; }

    public decimal? CapacityMax { get; init; }

    /// <summary>Minimum average stars. Profiles with no ratings yet drop out once this is set.</summary>
    public double? MinRating { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    /// <summary>"match" (default blend), "rating", "capacity", or "newest".</summary>
    public string SortBy { get; init; } = "match";
}

public record LocationOptionDto(string Country, string State, string[] Districts);

public interface IDiscoveryService
{
    Task<PagedResultDto<OpportunityRowDto>> SearchAsync(Profile viewer, DiscoveryQuery query,
        CancellationToken ct = default);

    /// <summary>Distinct locations present in discoverable profiles, for the filter bar.</summary>
    Task<IReadOnlyList<LocationOptionDto>> LocationOptionsAsync(ProfileRole counterpartyRole,
        CancellationToken ct = default);
}

public class DiscoveryService : IDiscoveryService
{
    private readonly SaakhDbContext _db;
    private readonly ITrustStatsService _trust;

    public DiscoveryService(SaakhDbContext db, ITrustStatsService trust)
    {
        _db = db;
        _trust = trust;
    }

    /// <summary>A Lender discovers Seekers and a Seeker discovers Lenders (spec, symmetric view).</summary>
    public static ProfileRole Opposite(ProfileRole role) =>
        role == ProfileRole.Lender ? ProfileRole.Seeker : ProfileRole.Lender;

    public async Task<PagedResultDto<OpportunityRowDto>> SearchAsync(Profile viewer, DiscoveryQuery query,
        CancellationToken ct = default)
    {
        var targetRole = Opposite(viewer.Role);

        var candidates = _db.Profiles.AsNoTracking()
            .Include(p => p.CategorySubType)
            .Where(p => p.Role == targetRole
                        && p.Id != viewer.Id
                        // Only Active profiles appear; Inactive, Suspended and Removed are
                        // excluded from results entirely (spec, Discovery).
                        && p.VerificationStatus == VerificationStatus.Active
                        && p.AvailabilityStatus == AvailabilityStatus.Active);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            candidates = candidates.Where(p =>
                EF.Functions.Like(p.Name, "%" + term + "%") ||
                EF.Functions.Like(p.District, "%" + term + "%") ||
                EF.Functions.Like(p.State, "%" + term + "%"));
        }

        if (!string.IsNullOrWhiteSpace(query.Country))
        {
            candidates = candidates.Where(p => p.Country == query.Country);
        }

        if (query.States is { Length: > 0 })
        {
            candidates = candidates.Where(p => query.States.Contains(p.State));
        }

        if (query.Districts is { Length: > 0 })
        {
            candidates = candidates.Where(p => query.Districts.Contains(p.District));
        }

        if (query.Categories is { Length: > 0 })
        {
            candidates = candidates.Where(p => query.Categories.Contains(p.Category));
        }

        if (query.SubTypeIds is { Length: > 0 })
        {
            candidates = candidates.Where(p =>
                p.CategorySubTypeId != null && query.SubTypeIds.Contains(p.CategorySubTypeId.Value));
        }

        // Capacity is an overlap test against the declared range, not containment: a Lender
        // who can supply 10k-50k still matches a 20k requirement.
        if (query.CapacityMin is not null)
        {
            candidates = candidates.Where(p => p.CapacityMax >= query.CapacityMin);
        }

        if (query.CapacityMax is not null)
        {
            candidates = candidates.Where(p => p.CapacityMin <= query.CapacityMax);
        }

        var rows = await candidates.ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToArray();
        var trust = await _trust.ForAsync(ids, ct);

        // The viewer's own relationship to each candidate: two queries total, not two per row.
        var interests = await _db.Interests.AsNoTracking()
            .Where(i => (i.FromProfileId == viewer.Id && ids.Contains(i.ToProfileId))
                        || (i.ToProfileId == viewer.Id && ids.Contains(i.FromProfileId)))
            .Select(i => new { i.Id, i.FromProfileId, i.ToProfileId, i.Status })
            .ToListAsync(ct);

        var activeDealCounterparties = await _db.Deals.AsNoTracking()
            .Where(d => (d.State == DealState.Open || d.State == DealState.Progress)
                        && (d.LenderProfileId == viewer.Id || d.SeekerProfileId == viewer.Id))
            .Select(d => d.LenderProfileId == viewer.Id ? d.SeekerProfileId : d.LenderProfileId)
            .ToListAsync(ct);

        var activeSet = activeDealCounterparties.ToHashSet();

        var assembled = rows.Select(p =>
        {
            var stats = trust.TryGetValue(p.Id, out var t) ? t : TrustStatsService.Empty;
            var interest = interests.FirstOrDefault(i => i.FromProfileId == p.Id || i.ToProfileId == p.Id);

            return new OpportunityRowDto(
                p.ToSummary(stats),
                interest?.Status,
                interest?.Id,
                interest is not null && interest.FromProfileId == viewer.Id,
                activeSet.Contains(p.Id),
                Score(viewer, p, stats));
        }).ToList();

        if (query.MinRating is not null)
        {
            assembled = assembled
                .Where(r => r.Profile.Trust.AverageStars is not null
                            && r.Profile.Trust.AverageStars >= query.MinRating)
                .ToList();
        }

        assembled = query.SortBy switch
        {
            "rating" => assembled
                .OrderByDescending(r => r.Profile.Trust.AverageStars ?? -1)
                .ThenByDescending(r => r.Profile.Trust.CompletedDeals)
                .ToList(),
            "capacity" => assembled.OrderByDescending(r => r.Profile.CapacityMax).ToList(),
            "newest" => assembled.OrderByDescending(r => r.Profile.CreatedAt).ToList(),
            _ => assembled
                .OrderByDescending(r => r.MatchScore)
                .ThenByDescending(r => r.Profile.Trust.AverageStars ?? -1)
                .ToList()
        };

        var total = assembled.Count;
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);

        var items = assembled.Skip((page - 1) * size).Take(size).ToList();
        return new PagedResultDto<OpportunityRowDto>(items, total, page, size);
    }

    /// <summary>
    /// Default ranking: a blend of location proximity, matching supply/need category, and
    /// rating, so relevant candidates appear with no filter applied (spec, Discovery).
    /// </summary>
    private static double Score(Profile viewer, Profile candidate, TrustSummaryDto trust)
    {
        double score = 0;

        // Location proximity. Same district is the strongest signal, then same state.
        if (string.Equals(candidate.District, viewer.District, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.State, viewer.State, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }
        else if (string.Equals(candidate.State, viewer.State, StringComparison.OrdinalIgnoreCase))
        {
            score += 22;
        }
        else if (string.Equals(candidate.Country, viewer.Country, StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
        }

        // Supply/need match on the shared taxonomy: an exact sub-type beats the same top category.
        if (candidate.Category == viewer.Category)
        {
            score += 18;
            if (candidate.CategorySubTypeId is not null
                && candidate.CategorySubTypeId == viewer.CategorySubTypeId)
            {
                score += 14;
            }
        }

        // Rating contributes up to 28 points, so a strong settled history outranks proximity
        // alone but cannot by itself beat a local exact-category match.
        if (trust.AverageStars is { } stars)
        {
            score += stars / 5.0 * 22;
            // A longer settled history is a stronger signal than one lucky 5-star deal.
            score += Math.Min(trust.CompletedDeals, 6);
        }

        // Halts the profile triggered cost it ground, per the v1 halt-fault rule.
        score -= Math.Min(trust.HaltsAtFault * 4, 16);

        return Math.Round(score, 2);
    }

    public async Task<IReadOnlyList<LocationOptionDto>> LocationOptionsAsync(ProfileRole counterpartyRole,
        CancellationToken ct = default)
    {
        var rows = await _db.Profiles.AsNoTracking()
            .Where(p => p.Role == counterpartyRole
                        && p.VerificationStatus == VerificationStatus.Active
                        && p.AvailabilityStatus == AvailabilityStatus.Active)
            .Select(p => new { p.Country, p.State, p.District })
            .Distinct()
            .ToListAsync(ct);

        return rows
            .GroupBy(r => new { r.Country, r.State })
            .OrderBy(g => g.Key.State)
            .Select(g => new LocationOptionDto(
                g.Key.Country,
                g.Key.State,
                g.Select(x => x.District).Distinct().OrderBy(d => d).ToArray()))
            .ToList();
    }
}
