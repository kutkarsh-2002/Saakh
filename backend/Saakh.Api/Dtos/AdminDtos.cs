using Saakh.Api.Domain;

namespace Saakh.Api.Dtos;

/// <summary>Admin user-directory filters: region, category type, status, profile type (spec, Admin).</summary>
public record AdminDirectoryQuery
{
    public string? Search { get; init; }
    public ProfileRole? Role { get; init; }
    public DealCategory? Category { get; init; }
    public int? CategorySubTypeId { get; init; }
    public VerificationStatus? VerificationStatus { get; init; }
    public AvailabilityStatus? AvailabilityStatus { get; init; }
    public string? State { get; init; }
    public string? District { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; } = true;
}

/// <summary>A verification-queue row: the profile plus everything needed to decide on it.</summary>
public record VerificationQueueRowDto(
    ProfileSummaryDto Profile,
    string Email,
    string? Phone,
    bool PhoneVerified,
    IReadOnlyList<EvidenceDocumentDto> Evidence,
    DateTimeOffset? SubmittedAt,
    // Hours since the evidence landed, so an Admin can triage against an SLA target.
    double? HoursWaiting);

public record ReviewVerificationDto(bool Approve, string? RejectionReason);

public record ModerationActionDto(
    AdminActionType ActionType,
    SuspensionDuration? SuspensionDuration,
    string? Notes);

public record AdminActionLogDto(
    Guid Id,
    Guid AdminId,
    string AdminName,
    Guid TargetProfileId,
    string TargetProfileName,
    AdminActionType ActionType,
    SuspensionDuration? SuspensionDuration,
    string? Notes,
    DateTimeOffset OccurredAt);

/// <summary>Top-of-console counters, so an Admin lands on the queue depth, not an empty page.</summary>
public record AdminOverviewDto(
    int TotalProfiles,
    int ActiveProfiles,
    int NeedsApproval,
    int PendingReview,
    int Rejected,
    int Suspended,
    int Removed,
    int OpenDeals,
    int HaltedDeals,
    int CompletedDeals);

public record AdminProfileDetailDto(
    ProfileSummaryDto Profile,
    string Email,
    bool PhoneVerified,
    IReadOnlyList<EvidenceDocumentDto> Evidence,
    IReadOnlyList<AdminActionLogDto> ActionLog,
    IReadOnlyList<DealRowDto> Deals);
