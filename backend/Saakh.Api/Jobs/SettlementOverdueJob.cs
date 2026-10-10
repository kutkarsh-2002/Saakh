using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Saakh.Api.Configuration;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Hubs;
using Saakh.Api.Services;

namespace Saakh.Api.Jobs;

/// <summary>
/// Acts on deals that have passed the settlement date both parties agreed to.
///
/// Two steps, deliberately separated. When the date passes, both parties are warned
/// once — most overdue deals are a few days late and settle themselves, and a platform
/// that closed them on the stroke of the deadline would be punishing ordinary trade.
/// When the grace period then runs out, the deal is closed and the outcome is recorded
/// against both records: they agreed that date together, and it is the agreement that
/// the trust record exists to report on.
///
/// This is not a halt. Nobody is marked as having triggered it, and no rating is
/// invented on anyone's behalf — the closure is counted as its own fact, next to the
/// stars counterparties actually gave.
/// </summary>
public class SettlementOverdueJob
{
    private readonly SaakhDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IRealtimePublisher _realtime;
    private readonly SettlementOptions _options;
    private readonly ILogger<SettlementOverdueJob> _log;

    public SettlementOverdueJob(SaakhDbContext db, INotificationService notifications,
        IRealtimePublisher realtime, IOptions<SettlementOptions> options,
        ILogger<SettlementOverdueJob> log)
    {
        _db = db;
        _notifications = notifications;
        _realtime = realtime;
        _options = options.Value;
        _log = log;
    }

    public async Task RunAsync()
    {
        var now = DateTimeOffset.UtcNow;

        await WarnAsync(now);

        if (_options.AutoCloseOverdue)
        {
            await CloseAsync(now);
        }
    }

    /// <summary>One warning each, the first time the agreed date is behind us.</summary>
    private async Task WarnAsync(DateTimeOffset now)
    {
        var due = await _db.Deals
            .Include(d => d.LenderProfile)
            .Include(d => d.SeekerProfile)
            .Where(d => (d.State == DealState.Open || d.State == DealState.Progress)
                        && d.EstimatedSettlementTime <= now
                        && d.OverdueWarningSentAt == null)
            .ToListAsync();

        foreach (var deal in due)
        {
            // A deal frozen by an administrator is one the parties cannot act on, so
            // warning them about a clock they cannot stop would be noise.
            if (IsFrozen(deal))
            {
                continue;
            }

            deal.OverdueWarningSentAt = now;

            var closesOn = deal.EstimatedSettlementTime.AddDays(_options.GraceDays);

            foreach (var party in new[] { deal.LenderProfileId, deal.SeekerProfileId })
            {
                await _notifications.PushAsync(party, NotificationKinds.SettlementOverdue,
                    $"Deal {deal.Reference} has passed its settlement date",
                    $"You both agreed to settle by {deal.EstimatedSettlementTime:d MMM yyyy}. "
                    + $"Confirm settlement from your side, or this deal closes on "
                    + $"{closesOn:d MMM yyyy} and is recorded on both of your trust records.",
                    $"/deals/{deal.Id}");
            }
        }

        if (due.Count > 0)
        {
            await _db.SaveChangesAsync();
            _log.LogInformation("Warned both parties on {Count} overdue deal(s)", due.Count);
        }
    }

    /// <summary>Closes what is still unsettled once the grace period has run out.</summary>
    private async Task CloseAsync(DateTimeOffset now)
    {
        var cutoff = now.AddDays(-_options.GraceDays);

        var expired = await _db.Deals
            .Include(d => d.LenderProfile)
            .Include(d => d.SeekerProfile)
            .Where(d => (d.State == DealState.Open || d.State == DealState.Progress)
                        && d.EstimatedSettlementTime <= cutoff)
            .ToListAsync();

        var closed = 0;

        foreach (var deal in expired)
        {
            if (IsFrozen(deal))
            {
                continue;
            }

            var from = deal.State;

            deal.State = DealState.Halted;
            deal.ClosedOverdue = true;
            deal.ClosedAt = now;
            // Left null on purpose: no party triggered this, so no party is marked as
            // having halted it. The overdue flag is what carries the finding.
            deal.HaltedByProfileId = null;
            deal.HaltedAt = now;

            _db.DealStateHistories.Add(new DealStateHistory
            {
                Id = Guid.NewGuid(),
                DealId = deal.Id,
                FromState = from,
                ToState = DealState.Halted,
                TriggeredByProfileId = null,
                Note = $"Closed by the platform: the agreed settlement date of "
                       + $"{deal.EstimatedSettlementTime:d MMM yyyy} passed and neither party "
                       + $"confirmed settlement within {_options.GraceDays} days. "
                       + "Recorded on both trust records."
            });

            foreach (var party in new[] { deal.LenderProfileId, deal.SeekerProfileId })
            {
                await _notifications.PushAsync(party, NotificationKinds.DealClosedOverdue,
                    $"Deal {deal.Reference} was closed unsettled",
                    "It passed the settlement date you both agreed and was not confirmed. "
                    + "This is now on both of your trust records.",
                    $"/deals/{deal.Id}");
            }

            await _realtime.ToDealAsync(deal.Id, HubEvents.DealStateChanged,
                new { dealId = deal.Id, state = DealState.Halted });

            closed++;
        }

        if (closed > 0)
        {
            await _db.SaveChangesAsync();
            _log.LogInformation("Closed {Count} deal(s) that passed their agreed settlement date", closed);
        }
    }

    /// <summary>
    /// A deal is frozen while either party is suspended or removed. The parties cannot
    /// settle it, so the platform does not hold them to the clock.
    /// </summary>
    private static bool IsFrozen(Deal deal) =>
        deal.LenderProfile.IsFrozen || deal.SeekerProfile.IsFrozen;
}
