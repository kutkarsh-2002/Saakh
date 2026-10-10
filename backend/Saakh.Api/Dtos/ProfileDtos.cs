using Saakh.Api.Domain;

namespace Saakh.Api.Dtos;

/// <summary>
/// The trust summary shown wherever a profile appears. Deliberately the full history rather
/// than one blended average: "12 deals, 11 rated 5 stars, 1 halted" (spec, Rating and Trust).
/// </summary>
public record TrustSummaryDto(
    int TotalDeals,
    int CompletedDeals,
    int HaltedDeals,
    int ActiveDeals,
    int RatingsReceived,
    double? AverageStars,
    // Count of 1-star to 5-star ratings received; index 0 is 1 star.
    int[] StarCounts,
    // Deals this profile was auto-flagged at fault for, by the v1 halt rule.
    int HaltsAtFault,
    // Deals the platform closed because the agreed settlement date passed. Recorded
    // against both parties, and kept apart from the star average so machine findings
    // are never mixed into what counterparties actually said.
    int DealsClosedOverdue);

public record CategorySubTypeDto(int Id, DealCategory Category, string Key, string DisplayName, string DefaultUnit);

/// <summary>Row shape for the Opportunity discovery table and profile cards.</summary>
public record ProfileSummaryDto(
    Guid Id,
    ProfileRole Role,
    string Name,
    bool IsBusiness,
    BusinessSize BusinessSize,
    bool GstinVerified,
    string? GstinMasked,
    string? GstinLegalName,
    VerificationStatus VerificationStatus,
    AvailabilityStatus AvailabilityStatus,
    DateTimeOffset? SuspensionEndDate,
    string? RejectionReason,
    string Country,
    string State,
    string District,
    DealCategory Category,
    CategorySubTypeDto? SubType,
    decimal CapacityMin,
    decimal CapacityMax,
    string CapacityUnit,
    string? OwnTradeDescription,
    string? Phone,
    DateTimeOffset CreatedAt,
    TrustSummaryDto Trust);

/// <summary>A discovery row: the counterparty plus the viewer's own relationship to them.</summary>
public record OpportunityRowDto(
    ProfileSummaryDto Profile,
    // Null when no interest has passed between the two profiles yet.
    InterestStatus? InterestStatus,
    Guid? InterestId,
    bool InterestWasSentByMe,
    // True when an Open or Progress deal already exists with this counterparty.
    bool HasActiveDeal,
    // Default-ranking score: location proximity + category match + rating (spec, Discovery).
    double MatchScore);

public record PagedResultDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record UpdateProfileDto(
    string Name,
    bool IsBusiness,
    BusinessSize BusinessSize,
    string Country,
    string State,
    string District,
    DealCategory Category,
    int? CategorySubTypeId,
    decimal CapacityMin,
    decimal CapacityMax,
    string CapacityUnit,
    string? OwnTradeDescription);

public record SetAvailabilityDto(bool Active);

/// <summary>Adds a GSTIN to an account that signed up without one.</summary>
public record AddGstinDto(string Gstin);

/// <summary>
/// The outcome of adding a GSTIN. <paramref name="Retrying"/> covers the case the
/// signup form already handles: the registry was unreachable, which is not the
/// vendor's fault, so the number is kept and a background job tries again.
/// </summary>
public record AddGstinResultDto(
    bool Verified,
    bool Retrying,
    string Message,
    ProfileSummaryDto Profile);

public record EvidenceDocumentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string DocumentType,
    DateTimeOffset SubmittedAt,
    EvidenceDecision Decision,
    string? RejectionReason,
    DateTimeOffset? ReviewedAt);

/// <summary>The verification banner payload: one source of truth for all four banner states.</summary>
public record VerificationStateDto(
    VerificationStatus Status,
    bool GstinVerified,
    string? RejectionReason,
    // False while the account is Needs Approval, Pending or Rejected.
    bool FeaturesUnlocked,
    IReadOnlyList<EvidenceDocumentDto> Evidence,
    DateTimeOffset? LastSubmittedAt);
