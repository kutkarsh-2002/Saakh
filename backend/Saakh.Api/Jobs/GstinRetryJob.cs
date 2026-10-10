using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Hubs;
using Saakh.Api.Services;

namespace Saakh.Api.Jobs;

/// <summary>
/// Retries a GSTIN lookup that failed for a transient reason (registry timeout, provider
/// 5xx) rather than because the number was wrong.
///
/// A signup whose GSTIN lookup times out is created on the no-GSTIN path, so the vendor is
/// never left stuck on a loading spinner; this job then upgrades them to Active without an
/// Admin ever having to look at it, if the registry comes back and confirms the number.
/// </summary>
public class GstinRetryJob
{
    private readonly SaakhDbContext _db;
    private readonly IGstinVerifier _verifier;
    private readonly INotificationService _notifications;
    private readonly IRealtimePublisher _realtime;
    private readonly ILogger<GstinRetryJob> _log;

    public GstinRetryJob(SaakhDbContext db, IGstinVerifier verifier, INotificationService notifications,
        IRealtimePublisher realtime, ILogger<GstinRetryJob> log)
    {
        _db = db;
        _verifier = verifier;
        _notifications = notifications;
        _realtime = realtime;
        _log = log;
    }

    /// <summary>Retries one profile's pending GSTIN. Hangfire re-runs it on an unhandled throw.</summary>
    public async Task RetryAsync(Guid profileId)
    {
        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.Id == profileId);

        if (profile?.Gstin is null || profile.GstinVerifiedAt is not null)
        {
            return;
        }

        var result = await _verifier.VerifyAsync(profile.Gstin);

        if (result.TransientFailure)
        {
            // Let Hangfire's retry policy handle the backoff rather than inventing one here.
            throw new InvalidOperationException(
                $"GSTIN registry still unavailable for profile {profileId}: {result.Message}");
        }

        if (!result.IsValid)
        {
            _log.LogInformation("GSTIN retry for {ProfileId} came back invalid: {Message}",
                profileId, result.Message);

            // The number turned out to be wrong, so the account stays on the manual-review
            // path with the GSTIN cleared, rather than silently claiming a verified identity.
            profile.Gstin = null;
            await _db.SaveChangesAsync();

            await _notifications.PushAsync(profile.Id, NotificationKinds.VerificationRejected,
                "We could not verify your GSTIN",
                $"{result.Message} Submit proof documents instead, and an administrator will review them.",
                "/profile");
            return;
        }

        profile.GstinVerifiedAt = DateTimeOffset.UtcNow;
        profile.GstinLegalName = result.LegalName;
        profile.VerificationStatus = VerificationStatus.Active;
        profile.RejectionReason = null;

        // The signup started Inactive because it was unapproved; the registry has now
        // approved it, so it becomes visible.
        if (profile.AvailabilityStatus == AvailabilityStatus.Inactive)
        {
            profile.AvailabilityStatus = AvailabilityStatus.Active;
        }

        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync();

        await _notifications.PushAsync(profile.Id, NotificationKinds.VerificationApproved,
            "Your GSTIN has been verified",
            "Your identity is confirmed against the government registry. You now have full access.",
            "/dashboard");

        await _realtime.ToProfileAsync(profile.Id, HubEvents.VerificationChanged,
            new { status = VerificationStatus.Active });
    }

    /// <summary>
    /// Scheduled sweep that picks up any profile still carrying an unverified GSTIN, for the
    /// case where the per-signup retry itself was lost (process restart mid-backoff).
    /// </summary>
    public async Task SweepAsync()
    {
        var pending = await _db.Profiles
            .Where(p => p.Gstin != null && p.GstinVerifiedAt == null)
            .Select(p => p.Id)
            .ToListAsync();

        foreach (var id in pending)
        {
            try
            {
                await RetryAsync(id);
            }
            catch (Exception ex)
            {
                // One unavailable lookup must not abort the whole sweep.
                _log.LogWarning(ex, "GSTIN sweep could not resolve profile {ProfileId}", id);
            }
        }
    }
}

/// <summary>Lifts suspensions whose window has passed, so a time-bound ban really is time-bound.</summary>
public class SuspensionExpiryJob
{
    private readonly SaakhDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ILogger<SuspensionExpiryJob> _log;

    public SuspensionExpiryJob(SaakhDbContext db, INotificationService notifications,
        ILogger<SuspensionExpiryJob> log)
    {
        _db = db;
        _notifications = notifications;
        _log = log;
    }

    public async Task RunAsync()
    {
        var now = DateTimeOffset.UtcNow;

        var expired = await _db.Profiles
            .Where(p => p.AvailabilityStatus == AvailabilityStatus.Suspended
                        && p.SuspensionEndDate != null
                        && p.SuspensionEndDate <= now)
            .ToListAsync();

        foreach (var profile in expired)
        {
            profile.AvailabilityStatus = AvailabilityStatus.Active;
            profile.SuspensionEndDate = null;
            profile.UpdatedAt = now;
        }

        if (expired.Count == 0)
        {
            return;
        }

        await _db.SaveChangesAsync();

        foreach (var profile in expired)
        {
            await _notifications.PushAsync(profile.Id, NotificationKinds.Moderation,
                "Your suspension has ended",
                "Your profile is visible in search again and your deals are no longer frozen.",
                "/dashboard");
        }

        _log.LogInformation("Lifted {Count} expired suspension(s)", expired.Count);
    }
}
