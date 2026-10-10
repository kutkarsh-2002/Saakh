using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Saakh.Api.Configuration;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Mapping;
using Saakh.Api.Hubs;
using Saakh.Api.Jobs;

namespace Saakh.Api.Services;

public interface IProfileService
{
    Task<ProfileSummaryDto> MineAsync(Profile actor, CancellationToken ct = default);

    Task<ProfileSummaryDto> UpdateAsync(Profile actor, UpdateProfileDto dto, CancellationToken ct = default);

    /// <summary>Self-service Active/Inactive toggle from account settings (spec, Profile).</summary>
    Task<ProfileSummaryDto> SetAvailabilityAsync(Profile actor, bool active, CancellationToken ct = default);

    /// <summary>
    /// Adds a GSTIN to an account that signed up without one. A verified number
    /// is the same proof of identity it is at signup, so it opens the account
    /// immediately rather than sending it to the review queue.
    /// </summary>
    Task<AddGstinResultDto> AddGstinAsync(Profile actor, string gstin, CancellationToken ct = default);

    Task<ProfileSummaryDto> PublicAsync(Guid profileId, CancellationToken ct = default);

    Task<VerificationStateDto> VerificationStateAsync(Profile actor, CancellationToken ct = default);

    Task<VerificationStateDto> SubmitEvidenceAsync(Profile actor, IReadOnlyList<EvidenceUpload> uploads,
        CancellationToken ct = default);

    Task<IReadOnlyList<CategorySubTypeDto>> TaxonomyAsync(CancellationToken ct = default);
}

public record EvidenceUpload(string FileName, string ContentType, long Length, string DocumentType, Stream Content);

public class ProfileService : IProfileService
{
    private readonly SaakhDbContext _db;
    private readonly ITrustStatsService _trust;
    private readonly IFileStorageService _storage;
    private readonly IRealtimePublisher _realtime;
    private readonly IGstinVerifier _gstin;
    private readonly INotificationService _notifications;
    private readonly IBackgroundJobClient _jobs;
    private readonly StorageOptions _storageOptions;

    public ProfileService(SaakhDbContext db, ITrustStatsService trust,
        IFileStorageService storage, IRealtimePublisher realtime,
        IGstinVerifier gstin, INotificationService notifications, IBackgroundJobClient jobs,
        IOptions<StorageOptions> storageOptions)
    {
        _db = db;
        _trust = trust;
        _storage = storage;
        _realtime = realtime;
        _gstin = gstin;
        _notifications = notifications;
        _jobs = jobs;
        _storageOptions = storageOptions.Value;
    }

    public async Task<ProfileSummaryDto> MineAsync(Profile actor, CancellationToken ct = default)
    {
        var trust = await _trust.ForOneAsync(actor.Id, ct);
        return actor.ToSummary(trust, revealPrivateDetails: true);
    }

    public async Task<ProfileSummaryDto> UpdateAsync(Profile actor, UpdateProfileDto dto,
        CancellationToken ct = default)
    {
        if (actor.AvailabilityStatus == AvailabilityStatus.Removed)
        {
            throw DomainException.Forbidden("This profile has been removed and can no longer be edited.");
        }

        var subType = dto.CategorySubTypeId is null
            ? null
            : await _db.CategorySubTypes.FirstOrDefaultAsync(s => s.Id == dto.CategorySubTypeId, ct)
              ?? throw new DomainException("That category sub-type does not exist.");

        if (subType is not null && subType.Category != dto.Category)
        {
            throw new DomainException($"\"{subType.DisplayName}\" is not a sub-type of {dto.Category}.");
        }

        if (dto.CapacityMax < dto.CapacityMin)
        {
            throw new DomainException("The upper end of the capacity range cannot be below the lower end.");
        }

        actor.Name = dto.Name.Trim();
        // A verified GSTIN *is* a business registration, so the answer is settled and the
        // client does not offer the choice. Enforced here too: the API is the boundary.
        actor.IsBusiness = actor.GstinVerifiedAt is not null || dto.IsBusiness;
        actor.BusinessSize = dto.BusinessSize;
        actor.Country = dto.Country.Trim();
        actor.State = dto.State.Trim();
        actor.District = dto.District.Trim();
        actor.Category = dto.Category;
        actor.CategorySubTypeId = subType?.Id;
        actor.CapacityMin = dto.CapacityMin;
        actor.CapacityMax = dto.CapacityMax;
        actor.CapacityUnit = string.IsNullOrWhiteSpace(dto.CapacityUnit)
            ? subType?.DefaultUnit ?? "INR"
            : dto.CapacityUnit.Trim();
        actor.OwnTradeDescription = string.IsNullOrWhiteSpace(dto.OwnTradeDescription)
            ? null
            : dto.OwnTradeDescription.Trim();
        actor.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _db.Entry(actor).Reference(p => p.CategorySubType).LoadAsync(ct);

        var trust = await _trust.ForOneAsync(actor.Id, ct);
        return actor.ToSummary(trust, revealPrivateDetails: true);
    }

    public async Task<ProfileSummaryDto> SetAvailabilityAsync(Profile actor, bool active,
        CancellationToken ct = default)
    {
        // An Admin suspension is not something the user can toggle their way out of.
        if (actor.AvailabilityStatus is AvailabilityStatus.Suspended or AvailabilityStatus.Removed)
        {
            throw DomainException.Forbidden(
                "An administrator has restricted this profile. The Active/Inactive toggle is unavailable.");
        }

        // Switching to Active before approval would claim a visibility the verification
        // gate does not grant, which is a worse answer than refusing.
        if (active && actor.VerificationStatus != VerificationStatus.Active)
        {
            throw new DomainException(
                "Your account is not approved yet, so it cannot appear in search. "
                + "It becomes visible on its own as soon as it is approved.");
        }

        // Existing in-flight deals continue unaffected either way (spec, Profile).
        actor.AvailabilityStatus = active ? AvailabilityStatus.Active : AvailabilityStatus.Inactive;
        actor.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var trust = await _trust.ForOneAsync(actor.Id, ct);
        return actor.ToSummary(trust, revealPrivateDetails: true);
    }

    public async Task<AddGstinResultDto> AddGstinAsync(Profile actor, string gstin,
        CancellationToken ct = default)
    {
        if (actor.AvailabilityStatus == AvailabilityStatus.Removed)
        {
            throw DomainException.Forbidden("This profile has been removed and can no longer be edited.");
        }

        // A verified GSTIN is the identity the whole trust record is anchored to, so it
        // is added once and never swapped. This endpoint fills a gap, it does not edit.
        if (actor.GstinVerifiedAt is not null)
        {
            throw new DomainException(
                "Your GSTIN is already verified. It is the identity your trust record is "
                + "anchored to, so it cannot be changed.");
        }

        var normalized = GstinFormat.Normalize(gstin);

        if (!GstinFormat.IsWellFormed(normalized))
        {
            // Checked locally first so a typo never spends a provider credit.
            throw new DomainException("That is not a valid GSTIN. It is 15 characters, like 27AAPFU0939F1ZV.");
        }

        var taken = await _db.Profiles
            .AnyAsync(p => p.Id != actor.Id && p.Gstin == normalized && p.GstinVerifiedAt != null, ct);

        if (taken)
        {
            throw new DomainException("That GSTIN is already verified on another Saakh account.");
        }

        var check = await _gstin.VerifyAsync(normalized, ct);

        if (check.TransientFailure)
        {
            // The registry was unreachable, which is not the vendor's fault. Keep the
            // number and let the retry job finish the job, exactly as signup does.
            actor.Gstin = normalized;
            actor.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _jobs.Schedule<GstinRetryJob>(job => job.RetryAsync(actor.Id), TimeSpan.FromMinutes(2));

            return new AddGstinResultDto(false, true,
                "The GSTIN registry is not responding right now. We have saved your number and "
                + "will keep trying — you do not need to do anything.",
                actor.ToSummary(await _trust.ForOneAsync(actor.Id, ct), revealPrivateDetails: true));
        }

        if (!check.IsValid)
        {
            throw new DomainException(check.Message ?? "That GSTIN could not be verified.");
        }

        var wasLocked = actor.VerificationStatus != VerificationStatus.Active;

        actor.Gstin = normalized;
        actor.GstinVerifiedAt = DateTimeOffset.UtcNow;
        actor.GstinLegalName = check.LegalName;
        // A verified GSTIN is a registered business by definition.
        actor.IsBusiness = true;
        actor.VerificationStatus = VerificationStatus.Active;
        actor.RejectionReason = null;

        // Verified by the registry is approved, so the account becomes visible.
        if (actor.AvailabilityStatus == AvailabilityStatus.Inactive)
        {
            actor.AvailabilityStatus = AvailabilityStatus.Active;
        }

        actor.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        if (wasLocked)
        {
            await _notifications.PushAsync(actor.Id, NotificationKinds.VerificationApproved,
                "Your GSTIN has been verified",
                "Your identity is confirmed against the government registry. You now have full access.",
                "/dashboard", ct);

            // Unlocks the tabs in the open tab without a reload, the same push an
            // administrator's approval sends.
            await _realtime.ToProfileAsync(actor.Id, HubEvents.VerificationChanged,
                new { status = VerificationStatus.Active });
        }

        var trust = await _trust.ForOneAsync(actor.Id, ct);

        return new AddGstinResultDto(true, false,
            check.LegalName is null
                ? "Your GSTIN is verified."
                : $"Verified against the registry as {check.LegalName}.",
            actor.ToSummary(trust, revealPrivateDetails: true));
    }

    public async Task<ProfileSummaryDto> PublicAsync(Guid profileId, CancellationToken ct = default)
    {
        var profile = await _db.Profiles.AsNoTracking()
                          .Include(p => p.CategorySubType)
                          .FirstOrDefaultAsync(p => p.Id == profileId, ct)
                      ?? throw DomainException.NotFound("That profile does not exist.");

        var trust = await _trust.ForOneAsync(profile.Id, ct);
        return profile.ToSummary(trust);
    }

    public async Task<VerificationStateDto> VerificationStateAsync(Profile actor, CancellationToken ct = default)
    {
        var evidence = await _db.EvidenceDocuments.AsNoTracking()
            .Where(e => e.ProfileId == actor.Id)
            .OrderByDescending(e => e.SubmittedAt)
            .ToListAsync(ct);

        return BuildVerificationState(actor, evidence);
    }

    public async Task<VerificationStateDto> SubmitEvidenceAsync(Profile actor,
        IReadOnlyList<EvidenceUpload> uploads, CancellationToken ct = default)
    {
        if (actor.GstinVerifiedAt is not null || actor.VerificationStatus == VerificationStatus.Active)
        {
            throw new DomainException("This account is already verified. No evidence is needed.");
        }

        if (uploads.Count == 0)
        {
            throw new DomainException("Attach at least one proof document.");
        }

        foreach (var upload in uploads)
        {
            if (upload.Length <= 0)
            {
                throw new DomainException($"\"{upload.FileName}\" is empty.");
            }

            if (upload.Length > _storageOptions.MaxFileSizeBytes)
            {
                var limitMb = _storageOptions.MaxFileSizeBytes / (1024 * 1024);
                throw new DomainException($"\"{upload.FileName}\" is larger than the {limitMb} MB limit.");
            }

            if (!_storageOptions.AllowedContentTypes.Contains(upload.ContentType))
            {
                throw new DomainException(
                    $"\"{upload.FileName}\" is not an accepted file type. Upload a photo (JPG, PNG, WEBP) or a PDF.");
            }
        }

        foreach (var upload in uploads)
        {
            var stored = await _storage.SaveAsync(upload.Content, upload.FileName, upload.ContentType, ct);

            _db.EvidenceDocuments.Add(new EvidenceDocument
            {
                Id = Guid.NewGuid(),
                ProfileId = actor.Id,
                StorageKey = stored.StorageKey,
                FileName = stored.FileName,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                DocumentType = string.IsNullOrWhiteSpace(upload.DocumentType)
                    ? "Unspecified"
                    : upload.DocumentType.Trim(),
                Decision = EvidenceDecision.AwaitingReview
            });
        }

        // Needs Approval and Rejected both move to Pending on submission; the banner updates
        // to reflect that it is under review, and tabs stay locked (spec, Verification).
        actor.VerificationStatus = VerificationStatus.Pending;
        actor.RejectionReason = null;
        actor.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _realtime.ToProfileAsync(actor.Id, HubEvents.VerificationChanged,
            new { status = VerificationStatus.Pending });
        await _realtime.ToAdminsAsync(HubEvents.VerificationChanged,
            new { profileId = actor.Id, status = VerificationStatus.Pending });

        return await VerificationStateAsync(actor, ct);
    }

    public async Task<IReadOnlyList<CategorySubTypeDto>> TaxonomyAsync(CancellationToken ct = default)
    {
        var rows = await _db.CategorySubTypes.AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        return rows.Select(ProfileMappers.ToDto).ToList();
    }

    public VerificationStateDto BuildVerificationState(Profile profile, IReadOnlyList<EvidenceDocument> evidence) =>
        new(
            profile.VerificationStatus,
            profile.GstinVerifiedAt is not null,
            profile.RejectionReason,
            profile.VerificationStatus == VerificationStatus.Active,
            evidence.Select(AdminMappers.ToDto).ToList(),
            evidence.Count == 0 ? null : evidence.Max(e => e.SubmittedAt));
}
