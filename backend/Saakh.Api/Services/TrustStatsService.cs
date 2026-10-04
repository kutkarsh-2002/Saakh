using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;

namespace Saakh.Api.Services;

/// <summary>
/// Computes the trust summary shown on every profile. Batched by profile id so a discovery
/// page of 25 rows costs two queries rather than fifty.
/// </summary>
public interface ITrustStatsService
{
    Task<IReadOnlyDictionary<Guid, TrustSummaryDto>> ForAsync(
        IReadOnlyCollection<Guid> profileIds, CancellationToken ct = default);

    Task<TrustSummaryDto> ForOneAsync(Guid profileId, CancellationToken ct = default);
}

public class TrustStatsService : ITrustStatsService
{
    private readonly SaakhDbContext _db;

    public TrustStatsService(SaakhDbContext db) => _db = db;

    public static TrustSummaryDto Empty => new(0, 0, 0, 0, 0, null, [0, 0, 0, 0, 0], 0);

    public async Task<TrustSummaryDto> ForOneAsync(Guid profileId, CancellationToken ct = default)
    {
        var map = await ForAsync([profileId], ct);
        return map.TryGetValue(profileId, out var stats) ? stats : Empty;
    }

    public async Task<IReadOnlyDictionary<Guid, TrustSummaryDto>> ForAsync(
        IReadOnlyCollection<Guid> profileIds, CancellationToken ct = default)
    {
        var ids = profileIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, TrustSummaryDto>();
        }

        var dealRows = await _db.Deals.AsNoTracking()
            .Where(d => ids.Contains(d.LenderProfileId) || ids.Contains(d.SeekerProfileId))
            .Select(d => new
            {
                d.LenderProfileId,
                d.SeekerProfileId,
                d.State,
                d.HaltedByProfileId
            })
            .ToListAsync(ct);

        var ratingRows = await _db.Ratings.AsNoTracking()
            .Where(r => ids.Contains(r.RatedProfileId))
            .Select(r => new { r.RatedProfileId, r.Stars })
            .ToListAsync(ct);

        var accumulators = ids.ToDictionary(id => id, _ => new Accumulator());

        foreach (var row in dealRows)
        {
            foreach (var side in new[] { row.LenderProfileId, row.SeekerProfileId })
            {
                if (!accumulators.TryGetValue(side, out var acc))
                {
                    continue;
                }

                acc.Total++;
                switch (row.State)
                {
                    case DealState.Completed:
                        acc.Completed++;
                        break;
                    case DealState.Halted:
                        acc.Halted++;
                        break;
                    default:
                        acc.Active++;
                        break;
                }

                // The v1 halt-fault rule: whoever triggered the halt carries it (spec, Rating).
                if (row.State == DealState.Halted && row.HaltedByProfileId == side)
                {
                    acc.HaltsAtFault++;
                }
            }
        }

        foreach (var rating in ratingRows)
        {
            if (accumulators.TryGetValue(rating.RatedProfileId, out var acc) && rating.Stars is >= 1 and <= 5)
            {
                acc.StarCounts[rating.Stars - 1]++;
                acc.StarSum += rating.Stars;
                acc.RatingCount++;
            }
        }

        return accumulators.ToDictionary(
            kv => kv.Key,
            kv => new TrustSummaryDto(
                kv.Value.Total,
                kv.Value.Completed,
                kv.Value.Halted,
                kv.Value.Active,
                kv.Value.RatingCount,
                kv.Value.RatingCount == 0 ? null : Math.Round((double)kv.Value.StarSum / kv.Value.RatingCount, 2),
                kv.Value.StarCounts,
                kv.Value.HaltsAtFault));
    }

    private class Accumulator
    {
        public int Total;
        public int Completed;
        public int Halted;
        public int Active;
        public int RatingCount;
        public int StarSum;
        public int HaltsAtFault;
        public readonly int[] StarCounts = new int[5];
    }
}
