using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;

namespace Saakh.Api.Services;

public interface IAnalyticsService
{
    /// <summary>Both dashboard charts, filtered to the logged-in user's own deals (spec, Analytics).</summary>
    Task<DashboardAnalyticsDto> ForProfileAsync(Guid profileId, int months = 9, CancellationToken ct = default);
}

public class AnalyticsService : IAnalyticsService
{
    private readonly SaakhDbContext _db;
    private readonly ITrustStatsService _trust;

    public AnalyticsService(SaakhDbContext db, ITrustStatsService trust)
    {
        _db = db;
        _trust = trust;
    }

    public async Task<DashboardAnalyticsDto> ForProfileAsync(Guid profileId, int months = 9,
        CancellationToken ct = default)
    {
        var rows = await _db.Deals.AsNoTracking()
            .Where(d => d.LenderProfileId == profileId || d.SeekerProfileId == profileId)
            .Select(d => new { d.CreatedAt, d.ClosedAt, d.State })
            .ToListAsync(ct);

        var window = BuildWindow(months);

        var openedVsClosed = new List<OpenedVsClosedPointDto>();
        var settlementQuality = new List<SettlementQualityPointDto>();

        var cumulativeOpened = 0;
        var cumulativeClosed = 0;

        // Deals opened before the window still count toward the running totals, otherwise
        // the first point would understate the backlog.
        var windowStart = window[0].Start;
        cumulativeOpened += rows.Count(r => r.CreatedAt < windowStart);
        cumulativeClosed += rows.Count(r => r.ClosedAt is not null && r.ClosedAt < windowStart);

        foreach (var bucket in window)
        {
            var openedThisPeriod = rows.Count(r => r.CreatedAt >= bucket.Start && r.CreatedAt < bucket.End);

            var closedThisPeriod = rows.Count(r =>
                r.ClosedAt is not null && r.ClosedAt >= bucket.Start && r.ClosedAt < bucket.End);

            cumulativeOpened += openedThisPeriod;
            cumulativeClosed += closedThisPeriod;

            // Chart 1: two running totals over time. The gap between them is backlog size,
            // not a rate of progress - the caveat lives in the UI copy next to the chart.
            openedVsClosed.Add(new OpenedVsClosedPointDto(bucket.Label, cumulativeOpened, cumulativeClosed));

            // Chart 2: per period, deals that closed Completed with no escalation versus
            // deals that were ever Halted. This is the health signal Chart 1 cannot give.
            var completedClean = rows.Count(r =>
                r.State == DealState.Completed
                && r.ClosedAt is not null && r.ClosedAt >= bucket.Start && r.ClosedAt < bucket.End);

            var halted = rows.Count(r =>
                r.State == DealState.Halted
                && r.ClosedAt is not null && r.ClosedAt >= bucket.Start && r.ClosedAt < bucket.End);

            settlementQuality.Add(new SettlementQualityPointDto(bucket.Label, completedClean, halted));
        }

        var trust = await _trust.ForOneAsync(profileId, ct);
        return new DashboardAnalyticsDto(openedVsClosed, settlementQuality, trust);
    }

    private record Bucket(string Label, DateTimeOffset Start, DateTimeOffset End);

    private static List<Bucket> BuildWindow(int months)
    {
        var count = Math.Clamp(months, 3, 24);
        var now = DateTimeOffset.UtcNow;
        var firstOfThisMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var buckets = new List<Bucket>();

        for (var offset = count - 1; offset >= 0; offset--)
        {
            var start = firstOfThisMonth.AddMonths(-offset);
            buckets.Add(new Bucket(start.ToString("MMM yy"), start, start.AddMonths(1)));
        }

        return buckets;
    }
}
