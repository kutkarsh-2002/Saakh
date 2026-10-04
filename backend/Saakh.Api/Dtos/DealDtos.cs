using Saakh.Api.Domain;

namespace Saakh.Api.Dtos;

public record SendInterestDto(Guid ToProfileId, string? Note);

public record RespondInterestDto(bool Accept);

public record InterestDto(
    Guid Id,
    ProfileSummaryDto Counterparty,
    // True when the logged-in profile is the one that sent the signal.
    bool SentByMe,
    InterestStatus Status,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RespondedAt,
    bool ChatUnlocked,
    // Set once a ticket has been raised off this interest.
    Guid? DealId,
    int UnreadMessages);

/// <summary>
/// The ticket raised once both sides agree. Branches by category: Money carries an amount and
/// currency, Raw Material a quantity, unit and material description (spec, Interest to Deal).
/// </summary>
public record RaiseTicketDto(
    Guid InterestId,
    DealCategory Category,
    int? CategorySubTypeId,
    decimal Capacity,
    string CapacityUnit,
    string? MaterialDescription,
    string Description,
    DateTimeOffset EstimatedSettlementTime);

public record DealStateHistoryDto(
    Guid Id,
    DealState? FromState,
    DealState ToState,
    Guid? TriggeredByProfileId,
    string? TriggeredByName,
    string? Note,
    DateTimeOffset OccurredAt);

public record MessageDto(
    Guid Id,
    Guid? DealId,
    Guid? InterestId,
    Guid SenderProfileId,
    string SenderName,
    string Body,
    DateTimeOffset SentAt,
    bool Mine);

public record SendMessageDto(string Body);

public record RatingDto(
    Guid Id,
    Guid DealId,
    Guid RaterProfileId,
    string RaterName,
    Guid RatedProfileId,
    int Stars,
    string? Comment,
    DateTimeOffset CreatedAt);

public record SubmitRatingDto(int Stars, string? Comment);

public record ResumeRequestDto(
    Guid Id,
    Guid DealId,
    Guid RequestedByProfileId,
    bool RequestedByMe,
    ResumeRequestStatus Status,
    DateTimeOffset CreatedAt);

/// <summary>Row shape for the Open Deals and History tables.</summary>
public record DealRowDto(
    Guid Id,
    string Reference,
    DealCategory Category,
    CategorySubTypeDto? SubType,
    string Description,
    decimal Capacity,
    string CapacityUnit,
    string? MaterialDescription,
    string Country,
    string State,
    string District,
    DateTimeOffset EstimatedSettlementTime,
    DealState DealState,
    // The other party, from the logged-in profile's perspective.
    ProfileSummaryDto Counterparty,
    // "Lender" or "Seeker": the side the logged-in profile sits on.
    ProfileRole MySide,
    bool IHaltedThisDeal,
    Guid? HaltedByProfileId,
    string? HaltedByName,
    DateTimeOffset? HaltedAt,
    bool MySettlementConfirmed,
    bool TheirSettlementConfirmed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    // Rating this profile gave on the deal, if any.
    RatingDto? RatingGiven,
    RatingDto? RatingReceived,
    bool CanRate,
    ResumeRequestDto? PendingResumeRequest,
    // True while either side is suspended or removed, freezing transitions (spec, Admin).
    bool Frozen);

/// <summary>Full deal workspace payload: ticket terms, timeline and chat in one call.</summary>
public record DealDetailDto(
    DealRowDto Deal,
    IReadOnlyList<DealStateHistoryDto> Timeline,
    IReadOnlyList<MessageDto> Messages,
    IReadOnlyList<RatingDto> Ratings,
    // Transitions the logged-in profile may trigger right now.
    IReadOnlyList<DealState> AllowedTransitions);

public record HaltDealDto(string? Reason);

public record AdvanceDealDto(string? Note);

/// <summary>
/// Chart 1: two cumulative running totals over time (spec, Analytics). Reported as-is, with
/// the caveat about reading the gap alone carried in the UI copy, not in the data.
/// </summary>
public record OpenedVsClosedPointDto(string Period, int CumulativeOpened, int CumulativeClosed);

/// <summary>Chart 2: per period, deals that closed clean versus deals that were ever halted.</summary>
public record SettlementQualityPointDto(string Period, int CompletedClean, int Halted);

public record DashboardAnalyticsDto(
    IReadOnlyList<OpenedVsClosedPointDto> OpenedVsClosed,
    IReadOnlyList<SettlementQualityPointDto> SettlementQuality,
    TrustSummaryDto Trust);

public record NotificationDto(
    Guid Id,
    string Kind,
    string Title,
    string Body,
    string? Link,
    bool IsRead,
    DateTimeOffset CreatedAt);
