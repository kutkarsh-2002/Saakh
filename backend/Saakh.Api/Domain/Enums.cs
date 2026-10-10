namespace Saakh.Api.Domain;

/// <summary>A Profile is exactly one of these. One login maps to one Profile (spec §4).</summary>
public enum ProfileRole
{
    Lender = 1,
    Seeker = 2
}

/// <summary>Identity verification tier (spec §5 state machine).</summary>
public enum VerificationStatus
{
    /// <summary>No-GSTIN account, no evidence submitted yet. Every tab except Profile is hidden.</summary>
    NeedsApproval = 1,

    /// <summary>Evidence submitted, awaiting Admin review. Tabs stay locked.</summary>
    Pending = 2,

    /// <summary>GSTIN validated at signup, or Admin-approved evidence. Full access.</summary>
    Active = 3,

    /// <summary>Admin rejected the evidence. Returns to Pending on resubmission.</summary>
    Rejected = 4
}

/// <summary>Self-chosen or Admin-imposed availability (spec §5 / §12).</summary>
public enum AvailabilityStatus
{
    Active = 1,

    /// <summary>Self-chosen, reversible at any time. Excluded from search.</summary>
    Inactive = 2,

    /// <summary>Admin-imposed, time-bound or permanent. Excluded from search, in-flight deals frozen.</summary>
    Suspended = 3,

    /// <summary>Admin-imposed. History retained for audit, Profile can no longer act.</summary>
    Removed = 4
}

/// <summary>Resource-agnostic deal category (spec §11 — must not assume money).</summary>
public enum DealCategory
{
    Money = 1,
    RawMaterial = 2
}

public enum InterestStatus
{
    Sent = 1,
    Accepted = 2,
    Declined = 3
}

/// <summary>Deal state machine (spec §8): Open → Progress → Completed, with Halted as a detour.</summary>
public enum DealState
{
    Open = 1,
    Progress = 2,
    Halted = 3,
    Completed = 4
}

public enum BusinessSize
{
    Individual = 1,
    Small = 2,
    Medium = 3,
    Large = 4
}

public enum EvidenceDecision
{
    AwaitingReview = 1,
    Approved = 2,
    Rejected = 3
}

/// <summary>Admin action types recorded in the AdminActionLog (spec §12).</summary>
public enum AdminActionType
{
    Warning = 1,
    Suspend = 2,
    Remove = 3,
    VerificationApproved = 4,
    VerificationRejected = 5,
    SuspensionLifted = 6
}

/// <summary>Fixed suspension windows offered by the Admin console (spec §12).</summary>
public enum SuspensionDuration
{
    OneWeek = 1,
    TwoWeeks = 2,
    OneMonth = 3,
    Permanent = 4
}

/// <summary>Both parties must agree before a Halted deal resumes (spec §11).</summary>
public enum ResumeRequestStatus
{
    Pending = 1,
    Accepted = 2,
    Declined = 3
}

/// <summary>
/// Where a set of proposed deal terms stands. A deal is only created once both
/// parties have agreed the terms, so this is the negotiation that precedes it.
/// </summary>
public enum ProposalStatus
{
    /// <summary>Waiting on the other party.</summary>
    Pending = 1,

    /// <summary>Agreed by both sides; the deal exists from this point.</summary>
    Accepted = 2,

    /// <summary>Answered with amended terms, which supersede these.</summary>
    Countered = 3,

    /// <summary>Taken back by whoever proposed it, before it was answered.</summary>
    Withdrawn = 4
}
