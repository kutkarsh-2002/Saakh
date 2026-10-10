using Saakh.Api.Domain;
using Saakh.Api.Dtos;

namespace Saakh.Api.Mapping;

/// <summary>
/// Entity to DTO mapping for deals, their chat and their ratings.
///
/// Half of a deal row depends on which party is looking at it — which side they
/// are on, whether they were the one who halted it, whose settlement
/// confirmation is outstanding — so the viewer is an explicit parameter
/// throughout rather than ambient state.
/// </summary>
public static class DealMappers
{
    public static MessageDto ToDto(this Message message, Guid viewerProfileId) =>
        new(
            message.Id,
            message.DealId,
            message.InterestId,
            message.SenderProfileId,
            message.SenderProfile?.Name ?? string.Empty,
            message.Body,
            message.SentAt,
            message.SenderProfileId == viewerProfileId);

    public static RatingDto ToDto(this Rating rating) =>
        new(
            rating.Id,
            rating.DealId,
            rating.RaterProfileId,
            rating.RaterProfile?.Name ?? string.Empty,
            rating.RatedProfileId,
            rating.Stars,
            rating.Comment,
            rating.CreatedAt);

    /// <summary>
    /// Terms, with whose turn it is worked out for the viewer rather than left to the
    /// client: both screens that show this have to agree on who can act.
    /// </summary>
    public static DealProposalDto ToDto(this DealProposal proposal, Guid viewerProfileId,
        TrustSummaryDto proposerTrust) =>
        new(
            proposal.Id,
            proposal.InterestId,
            proposal.ProposedByProfile.ToSummary(proposerTrust),
            proposal.ProposedByProfileId == viewerProfileId,
            proposal.Category,
            proposal.CategorySubType?.ToDto(),
            proposal.Capacity,
            proposal.CapacityUnit,
            proposal.MaterialDescription,
            proposal.Description,
            proposal.EstimatedSettlementTime,
            proposal.Status,
            proposal.Status == ProposalStatus.Pending
                && proposal.ProposedByProfileId != viewerProfileId,
            proposal.DealId,
            proposal.CreatedAt,
            proposal.RespondedAt);

    public static DealStateHistoryDto ToDto(this DealStateHistory entry) =>
        new(
            entry.Id,
            entry.FromState,
            entry.ToState,
            entry.TriggeredByProfileId,
            entry.TriggeredByProfile?.Name,
            entry.Note,
            entry.OccurredAt);

    public static ResumeRequestDto ToDto(this ResumeRequest request, Guid viewerProfileId) =>
        new(
            request.Id,
            request.DealId,
            request.RequestedByProfileId,
            request.RequestedByProfileId == viewerProfileId,
            request.Status,
            request.CreatedAt);

    /// <summary>
    /// A deal row seen from one party's perspective.
    /// </summary>
    /// <param name="deal">The deal, with both profiles, ratings and resume requests loaded.</param>
    /// <param name="viewerProfileId">The profile reading the row; decides every perspective field.</param>
    /// <param name="counterpartyTrust">
    /// Passed in so list endpoints can batch the trust lookup across every row.
    /// </param>
    public static DealRowDto ToRow(
        this Deal deal,
        Guid viewerProfileId,
        TrustSummaryDto counterpartyTrust)
    {
        var iAmLender = deal.LenderProfileId == viewerProfileId;
        var counterparty = iAmLender ? deal.SeekerProfile : deal.LenderProfile;

        var ratingGiven = deal.Ratings.FirstOrDefault(r => r.RaterProfileId == viewerProfileId);
        var ratingReceived = deal.Ratings.FirstOrDefault(r => r.RatedProfileId == viewerProfileId);

        // Either party being restricted freezes the deal for both of them.
        var frozen = (deal.LenderProfile?.IsFrozen ?? false) || (deal.SeekerProfile?.IsFrozen ?? false);

        var pendingResume = deal.ResumeRequests
            .Where(r => r.Status == ResumeRequestStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault();

        return new DealRowDto(
            deal.Id,
            deal.Reference,
            deal.Category,
            deal.CategorySubType?.ToDto(),
            deal.Description,
            deal.Capacity,
            deal.CapacityUnit,
            deal.MaterialDescription,
            deal.Country,
            deal.LocationState,
            deal.District,
            deal.EstimatedSettlementTime,
            deal.State,
            counterparty!.ToSummary(counterpartyTrust),
            iAmLender ? ProfileRole.Lender : ProfileRole.Seeker,
            deal.HaltedByProfileId == viewerProfileId,
            deal.HaltedByProfileId,
            deal.HaltedByProfile?.Name,
            deal.HaltedAt,
            iAmLender ? deal.LenderConfirmedSettlement : deal.SeekerConfirmedSettlement,
            iAmLender ? deal.SeekerConfirmedSettlement : deal.LenderConfirmedSettlement,
            deal.CreatedAt,
            deal.ClosedAt,
            ratingGiven?.ToDto(),
            ratingReceived?.ToDto(),
            // Rating opens once the deal is Completed or Halted, one per party.
            deal.IsClosed && ratingGiven is null,
            pendingResume?.ToDto(viewerProfileId),
            frozen,
            deal.ClosedOverdue);
    }
}
