using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Saakh.Api.Configuration;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Mapping;
using Saakh.Api.Hubs;

namespace Saakh.Api.Services;

public interface IProfileService
{
    Task<ProfileSummaryDto> MineAsync(Profile actor, CancellationToken ct = default);

    Task<ProfileSummaryDto> UpdateAsync(Profile actor, UpdateProfileDto dto, CancellationToken ct = default);

    /// <summary>Self-service Active/Inactive toggle from account settings (spec, Profile).</summary>
    Task<ProfileSummaryDto> SetAvailabilityAsync(Profile actor, bool active, CancellationToken ct = default);

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
    private readonly StorageOptions _storageOptions;

    public ProfileService(SaakhDbContext db, ITrustStatsService trust,
        IFileStorageService storage, IRealtimePublisher realtime,
        IOptions<StorageOptions> storageOptions)
    {
        _db = db;
        _trust = trust;
        _storage = storage;
        _realtime = realtime;
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
        actor.IsBusiness = dto.IsBusiness;
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

        // Existing in-flight deals continue unaffected either way (spec, Profile).
        actor.AvailabilityStatus = active ? AvailabilityStatus.Active : AvailabilityStatus.Inactive;
        actor.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var trust = await _trust.ForOneAsync(actor.Id, ct);
        return actor.ToSummary(trust, revealPrivateDetails: true);
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
