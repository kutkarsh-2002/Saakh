using Hangfire;
using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Mapping;
using Saakh.Api.Hubs;
using Saakh.Api.Jobs;

namespace Saakh.Api.Services;

public interface IAdminService
{
    Task<AdminOverviewDto> OverviewAsync(CancellationToken ct = default);

    Task<PagedResultDto<ProfileSummaryDto>> DirectoryAsync(AdminDirectoryQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<VerificationQueueRowDto>> VerificationQueueAsync(CancellationToken ct = default);

    Task<AdminProfileDetailDto> ProfileDetailAsync(Guid profileId, CancellationToken ct = default);

    Task<ProfileSummaryDto> ReviewVerificationAsync(Admin admin, Guid profileId, ReviewVerificationDto dto,
        CancellationToken ct = default);

    Task<ProfileSummaryDto> ModerateAsync(Admin admin, Guid profileId, ModerationActionDto dto,
        CancellationToken ct = default);

    Task<IReadOnlyList<AdminActionLogDto>> ActionLogAsync(Guid? profileId, CancellationToken ct = default);

    Task<(Stream Content, string ContentType, string FileName)> OpenEvidenceAsync(Guid evidenceId,
        CancellationToken ct = default);
}

public class AdminService : IAdminService
{
    private readonly SaakhDbContext _db;
    private readonly ITrustStatsService _trust;
    private readonly IFileStorageService _storage;
    private readonly INotificationService _notifications;
    private readonly IRealtimePublisher _realtime;
    private readonly IBackgroundJobClient _jobs;

    public AdminService(SaakhDbContext db, ITrustStatsService trust,
        IFileStorageService storage, INotificationService notifications, IRealtimePublisher realtime,
        IBackgroundJobClient jobs)
    {
        _db = db;
        _trust = trust;
        _storage = storage;
        _notifications = notifications;
        _realtime = realtime;
        _jobs = jobs;
    }

    public async Task<AdminOverviewDto> OverviewAsync(CancellationToken ct = default)
    {
        var profiles = await _db.Profiles.AsNoTracking()
            .GroupBy(p => new { p.VerificationStatus, p.AvailabilityStatus })
            .Select(g => new { g.Key.VerificationStatus, g.Key.AvailabilityStatus, Count = g.Count() })
            .ToListAsync(ct);

        var deals = await _db.Deals.AsNoTracking()
            .GroupBy(d => d.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int ByVerification(VerificationStatus status) =>
            profiles.Where(p => p.VerificationStatus == status).Sum(p => p.Count);

        int ByAvailability(AvailabilityStatus status) =>
            profiles.Where(p => p.AvailabilityStatus == status).Sum(p => p.Count);

        int ByDeal(DealState state) => deals.FirstOrDefault(d => d.State == state)?.Count ?? 0;

        return new AdminOverviewDto(
            profiles.Sum(p => p.Count),
            profiles.Where(p => p.VerificationStatus == VerificationStatus.Active
                                && p.AvailabilityStatus == AvailabilityStatus.Active).Sum(p => p.Count),
            ByVerification(VerificationStatus.NeedsApproval),
            ByVerification(VerificationStatus.Pending),
            ByVerification(VerificationStatus.Rejected),
            ByAvailability(AvailabilityStatus.Suspended),
            ByAvailability(AvailabilityStatus.Removed),
            ByDeal(DealState.Open) + ByDeal(DealState.Progress),
            ByDeal(DealState.Halted),
            ByDeal(DealState.Completed));
    }

    public async Task<PagedResultDto<ProfileSummaryDto>> DirectoryAsync(AdminDirectoryQuery query,
        CancellationToken ct = default)
    {
        // The Admin directory sees every Profile, including the ones discovery hides.
        var rows = _db.Profiles.AsNoTracking().Include(p => p.CategorySubType).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(p =>
                EF.Functions.Like(p.Name, "%" + term + "%")
                || EF.Functions.Like(p.District, "%" + term + "%")
                || EF.Functions.Like(p.State, "%" + term + "%")
                || (p.Gstin != null && EF.Functions.Like(p.Gstin, "%" + term + "%")));
        }

        if (query.Role is not null)
        {
            rows = rows.Where(p => p.Role == query.Role);
        }

        if (query.Category is not null)
        {
            rows = rows.Where(p => p.Category == query.Category);
        }

        if (query.CategorySubTypeId is not null)
        {
            rows = rows.Where(p => p.CategorySubTypeId == query.CategorySubTypeId);
        }

        if (query.VerificationStatus is not null)
        {
            rows = rows.Where(p => p.VerificationStatus == query.VerificationStatus);
        }

        if (query.AvailabilityStatus is not null)
        {
            rows = rows.Where(p => p.AvailabilityStatus == query.AvailabilityStatus);
        }

        if (!string.IsNullOrWhiteSpace(query.State))
        {
            rows = rows.Where(p => p.State == query.State);
        }

        if (!string.IsNullOrWhiteSpace(query.District))
        {
            rows = rows.Where(p => p.District == query.District);
        }

        rows = (query.SortBy, query.SortDescending) switch
        {
            ("name", true) => rows.OrderByDescending(p => p.Name),
            ("name", false) => rows.OrderBy(p => p.Name),
            ("status", true) => rows.OrderByDescending(p => p.VerificationStatus).ThenBy(p => p.Name),
            ("status", false) => rows.OrderBy(p => p.VerificationStatus).ThenBy(p => p.Name),
            (_, false) => rows.OrderBy(p => p.CreatedAt),
            _ => rows.OrderByDescending(p => p.CreatedAt)
        };

        var total = await rows.CountAsync(ct);
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);

        var pageRows = await rows.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var trust = await _trust.ForAsync(pageRows.Select(p => p.Id).ToArray(), ct);

        var items = pageRows
            .Select(p => p.ToSummary(trust.GetValueOrDefault(p.Id, TrustStatsService.Empty),
                revealPrivateDetails: true))
            .ToList();

        return new PagedResultDto<ProfileSummaryDto>(items, total, page, size);
    }

    public async Task<IReadOnlyList<VerificationQueueRowDto>> VerificationQueueAsync(CancellationToken ct = default)
    {
        // The queue holds every no-GSTIN account awaiting a human checkpoint, with Pending
        // first because those are the ones that have actually submitted something to review.
        var profiles = await _db.Profiles.AsNoTracking()
            .Include(p => p.CategorySubType)
            .Include(p => p.User)
            .Include(p => p.EvidenceDocuments)
            .Where(p => p.VerificationStatus == VerificationStatus.Pending
                        || p.VerificationStatus == VerificationStatus.NeedsApproval
                        || p.VerificationStatus == VerificationStatus.Rejected)
            .ToListAsync(ct);

        var trust = await _trust.ForAsync(profiles.Select(p => p.Id).ToArray(), ct);

        return profiles
            .Select(p =>
            {
                var evidence = p.EvidenceDocuments.OrderByDescending(e => e.SubmittedAt).ToList();
                var submittedAt = evidence.Count == 0 ? (DateTimeOffset?)null : evidence.Max(e => e.SubmittedAt);

                return new VerificationQueueRowDto(
                    p.ToSummary(trust.GetValueOrDefault(p.Id, TrustStatsService.Empty),
                        revealPrivateDetails: true),
                    p.User.Email ?? string.Empty,
                    p.Phone ?? p.User.PhoneNumber,
                    p.User.PhoneVerified,
                    evidence.Select(AdminMappers.ToDto).ToList(),
                    submittedAt,
                    submittedAt is null
                        ? null
                        : Math.Round((DateTimeOffset.UtcNow - submittedAt.Value).TotalHours, 1));
            })
            .OrderBy(r => r.Profile.VerificationStatus == VerificationStatus.Pending ? 0 : 1)
            .ThenByDescending(r => r.HoursWaiting ?? -1)
            .ToList();
    }

    public async Task<AdminProfileDetailDto> ProfileDetailAsync(Guid profileId, CancellationToken ct = default)
    {
        var profile = await _db.Profiles.AsNoTracking()
                          .Include(p => p.CategorySubType)
                          .Include(p => p.User)
                          .Include(p => p.EvidenceDocuments)
                          .FirstOrDefaultAsync(p => p.Id == profileId, ct)
                      ?? throw DomainException.NotFound("That profile does not exist.");

        var deals = await _db.Deals.AsNoTracking()
            .Include(d => d.LenderProfile).ThenInclude(p => p.CategorySubType)
            .Include(d => d.SeekerProfile).ThenInclude(p => p.CategorySubType)
            .Include(d => d.CategorySubType)
            .Include(d => d.HaltedByProfile)
            .Include(d => d.Ratings).ThenInclude(r => r.RaterProfile)
            .Include(d => d.ResumeRequests)
            .Where(d => d.LenderProfileId == profileId || d.SeekerProfileId == profileId)
            .OrderByDescending(d => d.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(ct);

        var counterpartyIds = deals
            .Select(d => d.LenderProfileId == profileId ? d.SeekerProfileId : d.LenderProfileId)
            .Append(profileId)
            .Distinct()
            .ToArray();

        var trust = await _trust.ForAsync(counterpartyIds, ct);

        var log = await _db.AdminActionLogs.AsNoTracking()
            .Include(a => a.Admin)
            .Include(a => a.TargetProfile)
            .Where(a => a.TargetProfileId == profileId)
            .OrderByDescending(a => a.OccurredAt)
            .ToListAsync(ct);

        return new AdminProfileDetailDto(
            profile.ToSummary(trust.GetValueOrDefault(profileId, TrustStatsService.Empty),
                revealPrivateDetails: true),
            profile.User.Email ?? string.Empty,
            profile.User.PhoneVerified,
            profile.EvidenceDocuments.OrderByDescending(e => e.SubmittedAt)
                .Select(AdminMappers.ToDto).ToList(),
            log.Select(AdminMappers.ToDto).ToList(),
            deals.Select(d =>
            {
                var otherId = d.LenderProfileId == profileId ? d.SeekerProfileId : d.LenderProfileId;
                return d.ToRow(profileId,
                    trust.GetValueOrDefault(otherId, TrustStatsService.Empty));
            }).ToList());
    }

    public async Task<ProfileSummaryDto> ReviewVerificationAsync(Admin admin, Guid profileId,
        ReviewVerificationDto dto, CancellationToken ct = default)
    {
        var profile = await _db.Profiles
                          .Include(p => p.CategorySubType)
                          .Include(p => p.User)
                          .Include(p => p.EvidenceDocuments)
                          .FirstOrDefaultAsync(p => p.Id == profileId, ct)
                      ?? throw DomainException.NotFound("That profile does not exist.");

        if (profile.VerificationStatus == VerificationStatus.Active)
        {
            throw DomainException.Conflict("That profile is already verified.");
        }

        if (profile.VerificationStatus == VerificationStatus.NeedsApproval)
        {
            throw new DomainException(
                "This account has not submitted any evidence yet, so there is nothing to review.");
        }

        if (!dto.Approve && string.IsNullOrWhiteSpace(dto.RejectionReason))
        {
            // The spec requires a reason be shown to the user on rejection.
            throw new DomainException("A rejection needs a reason, which is shown to the user.");
        }

        var awaiting = profile.EvidenceDocuments
            .Where(e => e.Decision == EvidenceDecision.AwaitingReview)
            .ToList();

        foreach (var document in awaiting)
        {
            document.Decision = dto.Approve ? EvidenceDecision.Approved : EvidenceDecision.Rejected;
            document.ReviewedByAdminId = admin.Id;
            document.ReviewedAt = DateTimeOffset.UtcNow;
            document.RejectionReason = dto.Approve ? null : dto.RejectionReason!.Trim();
        }

        profile.VerificationStatus = dto.Approve ? VerificationStatus.Active : VerificationStatus.Rejected;
        profile.RejectionReason = dto.Approve ? null : dto.RejectionReason!.Trim();
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        _db.AdminActionLogs.Add(new AdminActionLog
        {
            Id = Guid.NewGuid(),
            AdminId = admin.Id,
            TargetProfileId = profile.Id,
            ActionType = dto.Approve
                ? AdminActionType.VerificationApproved
                : AdminActionType.VerificationRejected,
            Notes = dto.Approve ? null : dto.RejectionReason!.Trim()
        });

        await _db.SaveChangesAsync(ct);

        if (dto.Approve)
        {
            // The approval email goes out through Hangfire, so a slow mail provider never
            // holds up the Admin's review action (tech-stack.md).
            _jobs.Enqueue<ApprovalEmailJob>(job => job.SendAsync(profile.Id));

            await _notifications.PushAsync(profile.Id, NotificationKinds.VerificationApproved,
                "Your account has been approved",
                "You now have full access: search, send interest, and track deals.",
                "/dashboard", ct);
        }
        else
        {
            await _notifications.PushAsync(profile.Id, NotificationKinds.VerificationRejected,
                "Your verification was not approved",
                dto.RejectionReason!.Trim(),
                "/profile", ct);
        }

        await _realtime.ToProfileAsync(profile.Id, HubEvents.VerificationChanged,
            new { status = profile.VerificationStatus, rejectionReason = profile.RejectionReason });

        var trust = await _trust.ForOneAsync(profile.Id, ct);
        return profile.ToSummary(trust, revealPrivateDetails: true);
    }

    public async Task<ProfileSummaryDto> ModerateAsync(Admin admin, Guid profileId, ModerationActionDto dto,
        CancellationToken ct = default)
    {
        var profile = await _db.Profiles
                          .Include(p => p.CategorySubType)
                          .FirstOrDefaultAsync(p => p.Id == profileId, ct)
                      ?? throw DomainException.NotFound("That profile does not exist.");

        string title;
        string body;

        switch (dto.ActionType)
        {
            case AdminActionType.Warning:
                // Logged against the Profile, visible to Admins, not shown publicly (spec).
                title = "An administrator recorded a warning on your account";
                body = string.IsNullOrWhiteSpace(dto.Notes)
                    ? "Review the platform rules before your next deal."
                    : dto.Notes.Trim();
                break;

            case AdminActionType.Suspend:
                if (dto.SuspensionDuration is null)
                {
                    throw new DomainException("Pick a suspension window: 1 week, 2 weeks, 1 month, or permanent.");
                }

                profile.AvailabilityStatus = AvailabilityStatus.Suspended;
                profile.SuspensionEndDate = dto.SuspensionDuration switch
                {
                    SuspensionDuration.OneWeek => DateTimeOffset.UtcNow.AddDays(7),
                    SuspensionDuration.TwoWeeks => DateTimeOffset.UtcNow.AddDays(14),
                    SuspensionDuration.OneMonth => DateTimeOffset.UtcNow.AddMonths(1),
                    // Permanent carries no end date.
                    _ => null
                };

                title = "Your account has been suspended";
                body = profile.SuspensionEndDate is null
                    ? "Your profile is hidden from search permanently and cannot send or receive interest."
                    : $"Your profile is hidden from search until {profile.SuspensionEndDate:dd MMM yyyy}.";
                break;

            case AdminActionType.Remove:
                // Deal history is retained for audit, but the Profile can no longer act.
                profile.AvailabilityStatus = AvailabilityStatus.Removed;
                profile.SuspensionEndDate = null;
                title = "Your account has been removed";
                body = "Your profile can no longer act on the platform. Existing deal history is retained.";
                break;

            case AdminActionType.SuspensionLifted:
                if (profile.AvailabilityStatus != AvailabilityStatus.Suspended)
                {
                    throw new DomainException("That profile is not currently suspended.");
                }

                profile.AvailabilityStatus = AvailabilityStatus.Active;
                profile.SuspensionEndDate = null;
                title = "Your suspension has been lifted";
                body = "Your profile is visible in search again and your deals are no longer frozen.";
                break;

            default:
                throw new DomainException("That moderation action is not available here.");
        }

        profile.UpdatedAt = DateTimeOffset.UtcNow;

        _db.AdminActionLogs.Add(new AdminActionLog
        {
            Id = Guid.NewGuid(),
            AdminId = admin.Id,
            TargetProfileId = profile.Id,
            ActionType = dto.ActionType,
            SuspensionDuration = dto.SuspensionDuration,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
        });

        await _db.SaveChangesAsync(ct);

        await _notifications.PushAsync(profile.Id, NotificationKinds.Moderation, title, body, "/profile", ct);
        await _realtime.ToProfileAsync(profile.Id, HubEvents.ProfileModerated,
            new { availabilityStatus = profile.AvailabilityStatus, suspensionEndDate = profile.SuspensionEndDate });

        var trust = await _trust.ForOneAsync(profile.Id, ct);
        return profile.ToSummary(trust, revealPrivateDetails: true);
    }

    public async Task<IReadOnlyList<AdminActionLogDto>> ActionLogAsync(Guid? profileId,
        CancellationToken ct = default)
    {
        var rows = _db.AdminActionLogs.AsNoTracking()
            .Include(a => a.Admin)
            .Include(a => a.TargetProfile)
            .AsQueryable();

        if (profileId is not null)
        {
            rows = rows.Where(a => a.TargetProfileId == profileId);
        }

        var list = await rows.OrderByDescending(a => a.OccurredAt).Take(200).ToListAsync(ct);
        return list.Select(AdminMappers.ToDto).ToList();
    }

    public async Task<(Stream Content, string ContentType, string FileName)> OpenEvidenceAsync(Guid evidenceId,
        CancellationToken ct = default)
    {
        var document = await _db.EvidenceDocuments.AsNoTracking()
                           .FirstOrDefaultAsync(e => e.Id == evidenceId, ct)
                       ?? throw DomainException.NotFound("That document does not exist.");

        var stream = await _storage.OpenReadAsync(document.StorageKey, ct)
                     ?? throw DomainException.NotFound("That document is no longer in storage.");

        return (stream, document.ContentType, document.FileName);
    }
}
