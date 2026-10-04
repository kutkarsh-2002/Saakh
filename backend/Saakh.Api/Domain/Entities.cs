using Microsoft.AspNetCore.Identity;

namespace Saakh.Api.Domain;

/// <summary>
/// Login identity. Roles are held by ASP.NET Core Identity ("Lender", "Seeker", "Admin").
/// A Lender/Seeker user owns exactly one <see cref="Profile"/>; an Admin owns exactly one <see cref="Admin"/>.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>Phone OTP is mandatory at signup on both verification paths (spec §5).</summary>
    public bool PhoneVerified { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Profile? Profile { get; set; }
    public Admin? Admin { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

public class AppRole : IdentityRole<Guid>
{
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}

/// <summary>
/// Extensible sub-type taxonomy shared by Lenders and Seekers so supply and demand
/// speak one vocabulary and can be filtered consistently (spec §5).
/// </summary>
public class CategorySubType
{
    public int Id { get; set; }
    public DealCategory Category { get; set; }

    /// <summary>Stable machine key, e.g. "fruits".</summary>
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Unit figures of this sub-type are quoted in, e.g. "kg", "crates", "INR".</summary>
    public string DefaultUnit { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

/// <summary>The discovery identity: one row per role per business (spec §4, §11).</summary>
public class Profile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public ProfileRole Role { get; set; }

    /// <summary>Business name or individual name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Individual vs registered business. Drives the business-size reading of capacity.</summary>
    public bool IsBusiness { get; set; }

    public BusinessSize BusinessSize { get; set; } = BusinessSize.Individual;

    /// <summary>Null when the account took the no-GSTIN path. Real-time validated when present.</summary>
    public string? Gstin { get; set; }

    /// <summary>Set when a GSTIN was accepted by <c>IGstinVerifier</c> at signup.</summary>
    public DateTimeOffset? GstinVerifiedAt { get; set; }

    /// <summary>Legal name the registry returned for the GSTIN — a real, non-fabricated trust signal.</summary>
    public string? GstinLegalName { get; set; }

    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.NeedsApproval;

    /// <summary>Populated only while <see cref="VerificationStatus"/> is Rejected.</summary>
    public string? RejectionReason { get; set; }

    public AvailabilityStatus AvailabilityStatus { get; set; } = AvailabilityStatus.Active;

    /// <summary>Null for a permanent suspension; otherwise the moment the suspension lifts.</summary>
    public DateTimeOffset? SuspensionEndDate { get; set; }

    public string Country { get; set; } = "India";
    public string State { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;

    /// <summary>What a Lender provides / what a Seeker needs.</summary>
    public DealCategory Category { get; set; }

    public int? CategorySubTypeId { get; set; }
    public CategorySubType? CategorySubType { get; set; }

    /// <summary>Supply capacity (Lender) or receiving capacity (Seeker): lower bound.</summary>
    public decimal CapacityMin { get; set; }

    public decimal CapacityMax { get; set; }

    /// <summary>"INR" for Money, else a physical unit such as "kg" or "crates".</summary>
    public string CapacityUnit { get; set; } = "INR";

    /// <summary>Seeker-only context: the trade they themselves deal in (spec §5).</summary>
    public string? OwnTradeDescription { get; set; }

    public string? Phone { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<EvidenceDocument> EvidenceDocuments { get; set; } = new List<EvidenceDocument>();
    public ICollection<Interest> SentInterests { get; set; } = new List<Interest>();
    public ICollection<Interest> ReceivedInterests { get; set; } = new List<Interest>();
    public ICollection<Deal> LenderDeals { get; set; } = new List<Deal>();
    public ICollection<Deal> SeekerDeals { get; set; } = new List<Deal>();
    public ICollection<Rating> RatingsGiven { get; set; } = new List<Rating>();
    public ICollection<Rating> RatingsReceived { get; set; } = new List<Rating>();

    /// <summary>Only Active-verified, Active-availability profiles appear in discovery (spec §6).</summary>
    public bool IsDiscoverable =>
        VerificationStatus == VerificationStatus.Active && AvailabilityStatus == AvailabilityStatus.Active;

    /// <summary>A frozen profile cannot drive deal state transitions (spec §12).</summary>
    public bool IsFrozen =>
        AvailabilityStatus is AvailabilityStatus.Suspended or AvailabilityStatus.Removed;
}

/// <summary>A one-sided signal that one party wants to explore business with another (spec §7).</summary>
public class Interest
{
    public Guid Id { get; set; }

    public Guid FromProfileId { get; set; }
    public Profile FromProfile { get; set; } = null!;

    public Guid ToProfileId { get; set; }
    public Profile ToProfile { get; set; } = null!;

    public InterestStatus Status { get; set; } = InterestStatus.Sent;

    /// <summary>Optional note carried with the interest signal.</summary>
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Deal> Deals { get; set; } = new List<Deal>();

    /// <summary>Chat unlocks only once the interest is accepted (spec §7).</summary>
    public bool ChatUnlocked => Status == InterestStatus.Accepted;
}

/// <summary>The core tracked object: one credit arrangement between one Lender and one Seeker.</summary>
public class Deal
{
    public Guid Id { get; set; }

    /// <summary>Short human-quotable reference shown in tables, e.g. "SK-10428".</summary>
    public string Reference { get; set; } = string.Empty;

    public Guid LenderProfileId { get; set; }
    public Profile LenderProfile { get; set; } = null!;

    public Guid SeekerProfileId { get; set; }
    public Profile SeekerProfile { get; set; } = null!;

    /// <summary>The accepted Interest the ticket was raised from.</summary>
    public Guid? InterestId { get; set; }
    public Interest? Interest { get; set; }

    public DealCategory Category { get; set; }

    public int? CategorySubTypeId { get; set; }
    public CategorySubType? CategorySubType { get; set; }

    /// <summary>Amount for Money, quantity for Raw Material — kept generic per spec §11.</summary>
    public decimal Capacity { get; set; }

    /// <summary>"INR" for Money, else the physical unit.</summary>
    public string CapacityUnit { get; set; } = "INR";

    /// <summary>Raw Material only: what the material actually is.</summary>
    public string? MaterialDescription { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTimeOffset EstimatedSettlementTime { get; set; }

    public DealState State { get; set; } = DealState.Open;

    public string Country { get; set; } = "India";

    /// <summary>Geographic state. Named apart from <see cref="State"/>, which is the deal's lifecycle state.</summary>
    public string LocationState { get; set; } = string.Empty;

    public string District { get; set; } = string.Empty;

    /// <summary>Whoever triggered the halt is auto-flagged at fault (spec §9, v1 rule).</summary>
    public Guid? HaltedByProfileId { get; set; }
    public Profile? HaltedByProfile { get; set; }
    public DateTimeOffset? HaltedAt { get; set; }

    /// <summary>Settlement needs both parties to confirm before the deal moves to Completed.</summary>
    public bool LenderConfirmedSettlement { get; set; }
    public bool SeekerConfirmedSettlement { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }

    public ICollection<DealStateHistory> StateHistory { get; set; } = new List<DealStateHistory>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Rating> Ratings { get; set; } = new List<Rating>();
    public ICollection<ResumeRequest> ResumeRequests { get; set; } = new List<ResumeRequest>();

    public bool IsActiveDeal => State is DealState.Open or DealState.Progress;
    public bool IsClosed => State is DealState.Completed or DealState.Halted;
}

/// <summary>Audit row: who triggered each transition, when (spec §11).</summary>
public class DealStateHistory
{
    public Guid Id { get; set; }

    public Guid DealId { get; set; }
    public Deal Deal { get; set; } = null!;

    public DealState? FromState { get; set; }
    public DealState ToState { get; set; }

    /// <summary>Null when the transition was system- or Admin-driven rather than party-driven.</summary>
    public Guid? TriggeredByProfileId { get; set; }
    public Profile? TriggeredByProfile { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Chat message. Belongs to an Interest thread (pre-ticket negotiation) and, once a ticket
/// is raised, to the Deal thread. Persisted indefinitely (tech-stack.md).
/// </summary>
public class Message
{
    public Guid Id { get; set; }

    public Guid? DealId { get; set; }
    public Deal? Deal { get; set; }

    public Guid? InterestId { get; set; }
    public Interest? Interest { get; set; }

    public Guid SenderProfileId { get; set; }
    public Profile SenderProfile { get; set; } = null!;

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>One per party per Deal, created when the deal reaches Completed or Halted (spec §9).</summary>
public class Rating
{
    public Guid Id { get; set; }

    public Guid DealId { get; set; }
    public Deal Deal { get; set; } = null!;

    public Guid RaterProfileId { get; set; }
    public Profile RaterProfile { get; set; } = null!;

    public Guid RatedProfileId { get; set; }
    public Profile RatedProfile { get; set; } = null!;

    /// <summary>1–5.</summary>
    public int Stars { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Proof document submitted by a no-GSTIN account for Admin review (spec §5, §12).</summary>
public class EvidenceDocument
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;

    /// <summary>Storage key handed back by <c>IFileStorageService</c> — never the file bytes.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>What the user says this document is, e.g. "Shop licence".</summary>
    public string DocumentType { get; set; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? ReviewedByAdminId { get; set; }
    public Admin? ReviewedByAdmin { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    public EvidenceDecision Decision { get; set; } = EvidenceDecision.AwaitingReview;
    public string? RejectionReason { get; set; }
}

/// <summary>Separate account type — not a Lender or Seeker Profile, never appears in search (spec §12).</summary>
public class Admin
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<AdminActionLog> Actions { get; set; } = new List<AdminActionLog>();
}

/// <summary>Every moderation and verification decision, with actor + timestamp (tech-stack.md security notes).</summary>
public class AdminActionLog
{
    public Guid Id { get; set; }

    public Guid AdminId { get; set; }
    public Admin Admin { get; set; } = null!;

    public Guid TargetProfileId { get; set; }
    public Profile TargetProfile { get; set; } = null!;

    public AdminActionType ActionType { get; set; }

    public string? Notes { get; set; }

    /// <summary>Set for Suspend actions.</summary>
    public SuspensionDuration? SuspensionDuration { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A "Resume?" request raised from a Halted deal's history row. The deal only returns to
/// Progress once the other party accepts — a one-sided request changes nothing (spec §11).
/// </summary>
public class ResumeRequest
{
    public Guid Id { get; set; }

    public Guid DealId { get; set; }
    public Deal Deal { get; set; } = null!;

    public Guid RequestedByProfileId { get; set; }
    public Profile RequestedByProfile { get; set; } = null!;

    public ResumeRequestStatus Status { get; set; } = ResumeRequestStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
}

/// <summary>In-app notification fanned out over SignalR and persisted so it survives a reload.</summary>
public class Notification
{
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;

    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>Client-side route this notification points at, e.g. "/deals/{id}".</summary>
    public string? Link { get; set; }

    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
