using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Hubs;
using Saakh.Api.Mapping;

namespace Saakh.Api.Services;

/// <summary>
/// The negotiation that precedes a deal.
///
/// Raising a ticket proposes terms; it does not create the deal. Both parties have to
/// agree what is being traded, how much, and by when, because the settlement date is
/// the one term the platform later enforces on its own — closing a deal and marking
/// both records over a date only one side ever chose would not be defensible.
///
/// Disagreement is answered with a counter-proposal rather than a rejection: the
/// amended terms supersede the ones before them and the turn passes back. Nothing in
/// this chain touches either party's trust record. Failing to agree terms is not a
/// failure to trade, and the record exists to say how people trade.
/// </summary>
public interface IProposalService
{
    Task<DealProposalDto> ProposeAsync(Profile actor, RaiseTicketDto dto, CancellationToken ct = default);

    Task<ProposalThreadDto> ForInterestAsync(Profile actor, Guid interestId, CancellationToken ct = default);

    /// <summary>Agreeing the terms, which is what creates the deal in Open.</summary>
    Task<DealRowDto> AcceptAsync(Profile actor, Guid proposalId, CancellationToken ct = default);

    Task<DealProposalDto> CounterAsync(Profile actor, Guid proposalId, RaiseTicketDto dto,
        CancellationToken ct = default);

    Task<DealProposalDto> WithdrawAsync(Profile actor, Guid proposalId, CancellationToken ct = default);
}

public class ProposalService : IProposalService
{
    private readonly SaakhDbContext _db;
    private readonly IDealService _deals;
    private readonly ITrustStatsService _trust;
    private readonly INotificationService _notifications;
    private readonly IRealtimePublisher _realtime;

    public ProposalService(SaakhDbContext db, IDealService deals, ITrustStatsService trust,
        INotificationService notifications, IRealtimePublisher realtime)
    {
        _db = db;
        _deals = deals;
        _trust = trust;
        _notifications = notifications;
        _realtime = realtime;
    }

    private IQueryable<DealProposal> ProposalGraph => _db.DealProposals
        .Include(p => p.ProposedByProfile).ThenInclude(p => p.CategorySubType)
        .Include(p => p.Interest).ThenInclude(i => i.FromProfile)
        .Include(p => p.Interest).ThenInclude(i => i.ToProfile)
        .Include(p => p.CategorySubType);

    public async Task<DealProposalDto> ProposeAsync(Profile actor, RaiseTicketDto dto,
        CancellationToken ct = default)
    {
        var interest = await LoadInterestAsync(actor, dto.InterestId, ct);

        // One live proposal per interest: two sets of terms in flight at once is how two
        // parties end up each believing a different thing was agreed.
        var live = await _db.DealProposals
            .FirstOrDefaultAsync(p => p.InterestId == interest.Id && p.Status == ProposalStatus.Pending, ct);

        if (live is not null)
        {
            throw new DomainException(live.ProposedByProfileId == actor.Id
                ? "You already have terms waiting on the other party. Withdraw them to propose something different."
                : "They have put terms to you. Accept them, or answer with amended terms.");
        }

        if (await _db.Deals.AnyAsync(d => d.InterestId == interest.Id, ct))
        {
            throw new DomainException("This conversation already produced a deal.");
        }

        var proposal = await BuildAsync(actor, interest, dto, ct);

        _db.DealProposals.Add(proposal);
        await _db.SaveChangesAsync(ct);

        var other = OtherParty(interest, actor.Id);

        await _notifications.PushAsync(other.Id, NotificationKinds.DealRaised,
            $"{actor.Name} proposed terms",
            "Review what they are proposing and the date they are committing to. "
            + "The deal opens once you agree.",
            $"/interests/{interest.Id}", ct);

        await _realtime.ToProfileAsync(other.Id, HubEvents.InterestChanged,
            new { interestId = interest.Id, status = interest.Status });

        return await LoadDtoAsync(proposal.Id, actor.Id, ct);
    }

    public async Task<ProposalThreadDto> ForInterestAsync(Profile actor, Guid interestId,
        CancellationToken ct = default)
    {
        var interest = await LoadInterestAsync(actor, interestId, ct, requireAccepted: false);

        var proposals = await ProposalGraph
            .Where(p => p.InterestId == interest.Id)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

        var dtos = new List<DealProposalDto>(proposals.Count);

        foreach (var proposal in proposals)
        {
            dtos.Add(await ToDtoAsync(proposal, actor.Id, ct));
        }

        return new ProposalThreadDto(
            dtos.FirstOrDefault(d => d.Status == ProposalStatus.Pending),
            dtos);
    }

    public async Task<DealRowDto> AcceptAsync(Profile actor, Guid proposalId,
        CancellationToken ct = default)
    {
        var proposal = await LoadPendingAsync(actor, proposalId, ct);

        // Only the party who did not write the terms can agree them. Otherwise "agreed by
        // both parties" means nothing at all.
        if (proposal.ProposedByProfileId == actor.Id)
        {
            throw new DomainException("These are your own terms. The other party has to agree them.");
        }

        var row = await _deals.CreateFromProposalAsync(actor, proposal, ct);

        proposal.Status = ProposalStatus.Accepted;
        proposal.RespondedAt = DateTimeOffset.UtcNow;
        proposal.DealId = row.Id;

        await _db.SaveChangesAsync(ct);

        return row;
    }

    public async Task<DealProposalDto> CounterAsync(Profile actor, Guid proposalId, RaiseTicketDto dto,
        CancellationToken ct = default)
    {
        var proposal = await LoadPendingAsync(actor, proposalId, ct);

        if (proposal.ProposedByProfileId == actor.Id)
        {
            throw new DomainException(
                "These are your own terms. Withdraw them if you want to propose something different.");
        }

        var interest = proposal.Interest;
        var counter = await BuildAsync(actor, interest, dto with { InterestId = interest.Id }, ct);

        _db.DealProposals.Add(counter);
        await _db.SaveChangesAsync(ct);

        // Linked after the insert so the foreign key points at a row that exists.
        proposal.Status = ProposalStatus.Countered;
        proposal.RespondedAt = DateTimeOffset.UtcNow;
        proposal.SupersededByProposalId = counter.Id;

        await _db.SaveChangesAsync(ct);

        var other = OtherParty(interest, actor.Id);

        await _notifications.PushAsync(other.Id, NotificationKinds.DealRaised,
            $"{actor.Name} answered with different terms",
            "They have amended what you proposed. Review the changes and agree them, or answer again.",
            $"/interests/{interest.Id}", ct);

        await _realtime.ToProfileAsync(other.Id, HubEvents.InterestChanged,
            new { interestId = interest.Id, status = interest.Status });

        return await LoadDtoAsync(counter.Id, actor.Id, ct);
    }

    public async Task<DealProposalDto> WithdrawAsync(Profile actor, Guid proposalId,
        CancellationToken ct = default)
    {
        var proposal = await LoadPendingAsync(actor, proposalId, ct);

        if (proposal.ProposedByProfileId != actor.Id)
        {
            throw new DomainException("Only the party who proposed these terms can withdraw them.");
        }

        proposal.Status = ProposalStatus.Withdrawn;
        proposal.RespondedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        return await LoadDtoAsync(proposal.Id, actor.Id, ct);
    }

    // ---- helpers -------------------------------------------------------------------

    private async Task<Interest> LoadInterestAsync(Profile actor, Guid interestId, CancellationToken ct,
        bool requireAccepted = true)
    {
        var interest = await _db.Interests
            .Include(i => i.FromProfile)
            .Include(i => i.ToProfile)
            .FirstOrDefaultAsync(i => i.Id == interestId, ct)
            ?? throw DomainException.NotFound("That interest thread no longer exists.");

        if (interest.FromProfileId != actor.Id && interest.ToProfileId != actor.Id)
        {
            throw DomainException.Forbidden("You are not part of that conversation.");
        }

        if (requireAccepted && interest.Status != InterestStatus.Accepted)
        {
            throw new DomainException("Terms can only be proposed once the interest has been accepted.");
        }

        return interest;
    }

    private async Task<DealProposal> LoadPendingAsync(Profile actor, Guid proposalId, CancellationToken ct)
    {
        var proposal = await ProposalGraph.FirstOrDefaultAsync(p => p.Id == proposalId, ct)
            ?? throw DomainException.NotFound("Those terms no longer exist.");

        var interest = proposal.Interest;

        if (interest.FromProfileId != actor.Id && interest.ToProfileId != actor.Id)
        {
            throw DomainException.Forbidden("You are not part of that conversation.");
        }

        if (proposal.Status != ProposalStatus.Pending)
        {
            throw new DomainException(proposal.Status switch
            {
                ProposalStatus.Accepted => "These terms were already agreed.",
                ProposalStatus.Countered => "These terms were answered with amended ones.",
                _ => "These terms were withdrawn."
            });
        }

        return proposal;
    }

    private async Task<DealProposal> BuildAsync(Profile actor, Interest interest, RaiseTicketDto dto,
        CancellationToken ct)
    {
        var other = OtherParty(interest, actor.Id);

        if (actor.Role == other.Role)
        {
            throw new DomainException("A deal needs one Lender and one Seeker.");
        }

        if (other.IsFrozen)
        {
            throw new DomainException($"{other.Name} cannot take on new deals right now.");
        }

        if (dto.Category == DealCategory.RawMaterial && string.IsNullOrWhiteSpace(dto.MaterialDescription))
        {
            throw new DomainException("Tell the other party what the material is.");
        }

        // A date already in the past cannot be agreed to: the platform would close the
        // deal and mark both records the moment it was created.
        if (dto.EstimatedSettlementTime <= DateTimeOffset.UtcNow)
        {
            throw new DomainException("Pick a settlement date in the future.");
        }

        var subType = dto.CategorySubTypeId is null
            ? null
            : await _db.CategorySubTypes.FirstOrDefaultAsync(s => s.Id == dto.CategorySubTypeId, ct)
              ?? throw new DomainException("That category sub-type does not exist.");

        if (subType is not null && subType.Category != dto.Category)
        {
            throw new DomainException($"\"{subType.DisplayName}\" is not a sub-type of {dto.Category}.");
        }

        return new DealProposal
        {
            Id = Guid.NewGuid(),
            InterestId = interest.Id,
            ProposedByProfileId = actor.Id,
            Category = dto.Category,
            CategorySubTypeId = subType?.Id,
            Capacity = dto.Capacity,
            CapacityUnit = string.IsNullOrWhiteSpace(dto.CapacityUnit)
                ? subType?.DefaultUnit ?? "INR"
                : dto.CapacityUnit.Trim(),
            MaterialDescription = dto.Category == DealCategory.RawMaterial
                ? dto.MaterialDescription!.Trim()
                : null,
            Description = dto.Description.Trim(),
            EstimatedSettlementTime = dto.EstimatedSettlementTime,
            Status = ProposalStatus.Pending
        };
    }

    private static Profile OtherParty(Interest interest, Guid actorId) =>
        interest.FromProfileId == actorId ? interest.ToProfile : interest.FromProfile;

    private async Task<DealProposalDto> LoadDtoAsync(Guid proposalId, Guid viewerId, CancellationToken ct)
    {
        var proposal = await ProposalGraph.FirstAsync(p => p.Id == proposalId, ct);
        return await ToDtoAsync(proposal, viewerId, ct);
    }

    private async Task<DealProposalDto> ToDtoAsync(DealProposal proposal, Guid viewerId,
        CancellationToken ct)
    {
        var trust = await _trust.ForOneAsync(proposal.ProposedByProfileId, ct);
        return proposal.ToDto(viewerId, trust);
    }
}
