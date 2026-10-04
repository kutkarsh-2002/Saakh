using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Mapping;
using Saakh.Api.Hubs;

namespace Saakh.Api.Services;

public interface IDealService
{
    Task<DealRowDto> RaiseTicketAsync(Profile actor, RaiseTicketDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<DealRowDto>> OpenDealsAsync(Profile actor, CancellationToken ct = default);

    Task<IReadOnlyList<DealRowDto>> HistoryAsync(Profile actor, DealState? filter, CancellationToken ct = default);

    Task<DealDetailDto> GetAsync(Profile actor, Guid dealId, CancellationToken ct = default);

    Task<DealDetailDto> StartProgressAsync(Profile actor, Guid dealId, string? note, CancellationToken ct = default);

    Task<DealDetailDto> ConfirmSettlementAsync(Profile actor, Guid dealId, CancellationToken ct = default);

    Task<DealDetailDto> HaltAsync(Profile actor, Guid dealId, string? reason, CancellationToken ct = default);

    Task<ResumeRequestDto> RequestResumeAsync(Profile actor, Guid dealId, CancellationToken ct = default);

    Task<DealDetailDto> RespondToResumeAsync(Profile actor, Guid dealId, Guid requestId, bool accept,
        CancellationToken ct = default);

    Task<MessageDto> PostMessageAsync(Profile actor, Guid dealId, string body, CancellationToken ct = default);
}

public class DealService : IDealService
{
    private readonly SaakhDbContext _db;
    private readonly ITrustStatsService _trust;
    private readonly INotificationService _notifications;
    private readonly IRealtimePublisher _realtime;

    public DealService(SaakhDbContext db, ITrustStatsService trust,
        INotificationService notifications, IRealtimePublisher realtime)
    {
        _db = db;
        _trust = trust;
        _notifications = notifications;
        _realtime = realtime;
    }

    private IQueryable<Deal> DealGraph => _db.Deals
        .Include(d => d.LenderProfile).ThenInclude(p => p.CategorySubType)
        .Include(d => d.SeekerProfile).ThenInclude(p => p.CategorySubType)
        .Include(d => d.CategorySubType)
        .Include(d => d.HaltedByProfile)
        .Include(d => d.Ratings).ThenInclude(r => r.RaterProfile)
        .Include(d => d.ResumeRequests)
        // Several collection includes in one query multiply the rows together;
        // splitting keeps the row count linear in the number of deals.
        .AsSplitQuery();

    public async Task<DealRowDto> RaiseTicketAsync(Profile actor, RaiseTicketDto dto, CancellationToken ct = default)
    {
        RequireUnlocked(actor);

        var interest = await _db.Interests
            .Include(i => i.FromProfile)
            .Include(i => i.ToProfile)
            .FirstOrDefaultAsync(i => i.Id == dto.InterestId, ct)
            ?? throw DomainException.NotFound("That interest thread no longer exists.");

        if (interest.FromProfileId != actor.Id && interest.ToProfileId != actor.Id)
        {
            throw DomainException.Forbidden("You are not part of that conversation.");
        }

        // A ticket is only raised once both sides have agreed, which the platform models as
        // an accepted Interest (spec, Interest to Deal creation).
        if (interest.Status != InterestStatus.Accepted)
        {
            throw new DomainException("A ticket can only be raised once the interest has been accepted.");
        }

        var other = interest.FromProfileId == actor.Id ? interest.ToProfile : interest.FromProfile;

        if (actor.Role == other.Role)
        {
            throw new DomainException("A deal needs one Lender and one Seeker.");
        }

        if (other.IsFrozen)
        {
            throw new DomainException($"{other.Name} cannot take on new deals right now.");
        }

        var lender = actor.Role == ProfileRole.Lender ? actor : other;
        var seeker = actor.Role == ProfileRole.Seeker ? actor : other;

        var subType = await ResolveSubTypeAsync(dto.Category, dto.CategorySubTypeId, ct);

        if (dto.Category == DealCategory.RawMaterial && string.IsNullOrWhiteSpace(dto.MaterialDescription))
        {
            throw new DomainException("Tell the other party what the material is.");
        }

        // There is deliberately no cap on simultaneous Open/Progress deals one profile can
        // hold across different counterparties (spec, Discovery).
        var deal = new Deal
        {
            Id = Guid.NewGuid(),
            Reference = await NextReferenceAsync(ct),
            LenderProfileId = lender.Id,
            SeekerProfileId = seeker.Id,
            InterestId = interest.Id,
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
            // The deal is located where the Seeker is: that is where the material or money lands.
            Country = seeker.Country,
            LocationState = seeker.State,
            District = seeker.District,
            State = DealState.Open
        };

        _db.Deals.Add(deal);
        _db.DealStateHistories.Add(new DealStateHistory
        {
            Id = Guid.NewGuid(),
            DealId = deal.Id,
            FromState = null,
            ToState = DealState.Open,
            TriggeredByProfileId = actor.Id,
            Note = "Ticket raised, terms agreed."
        });

        await _db.SaveChangesAsync(ct);

        // Chat carries over from the interest thread into the deal workspace, so the
        // negotiation that led to the ticket stays readable alongside it. Runs after the
        // insert so the foreign key it writes already points at a row.
        await _db.Messages
            .Where(m => m.InterestId == interest.Id && m.DealId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DealId, deal.Id), ct);

        await _notifications.PushAsync(other.Id, NotificationKinds.DealRaised,
            $"{actor.Name} raised a ticket",
            $"Deal {deal.Reference} is now Open. Review the terms in the deal workspace.",
            $"/deals/{deal.Id}", ct);

        var row = await LoadRowAsync(deal.Id, actor.Id, ct);
        await PublishStateAsync(deal, ct);
        return row;
    }

    public async Task<IReadOnlyList<DealRowDto>> OpenDealsAsync(Profile actor, CancellationToken ct = default)
    {
        var deals = await DealGraph
            .Where(d => (d.LenderProfileId == actor.Id || d.SeekerProfileId == actor.Id)
                        && (d.State == DealState.Open || d.State == DealState.Progress))
            .OrderBy(d => d.EstimatedSettlementTime)
            .ToListAsync(ct);

        return await ToRowsAsync(deals, actor.Id, ct);
    }

    public async Task<IReadOnlyList<DealRowDto>> HistoryAsync(Profile actor, DealState? filter,
        CancellationToken ct = default)
    {
        // History lists every deal that has left the active states: Completed and Halted
        // (spec, History tab), with an optional toggle between the two.
        var query = DealGraph
            .Where(d => (d.LenderProfileId == actor.Id || d.SeekerProfileId == actor.Id)
                        && (d.State == DealState.Completed || d.State == DealState.Halted));

        if (filter is DealState.Completed or DealState.Halted)
        {
            query = query.Where(d => d.State == filter);
        }

        var deals = await query
            .OrderByDescending(d => d.ClosedAt ?? d.CreatedAt)
            .ToListAsync(ct);

        return await ToRowsAsync(deals, actor.Id, ct);
    }

    public async Task<DealDetailDto> GetAsync(Profile actor, Guid dealId, CancellationToken ct = default)
    {
        var deal = await RequireDealAsync(dealId, actor.Id, ct);
        return await BuildDetailAsync(deal, actor, ct);
    }

    public async Task<DealDetailDto> StartProgressAsync(Profile actor, Guid dealId, string? note,
        CancellationToken ct = default)
    {
        var deal = await RequireDealAsync(dealId, actor.Id, ct);
        RequireNotFrozen(deal);

        if (deal.State != DealState.Open)
        {
            throw new DomainException(
                $"Only an Open deal can move into Progress. This deal is {Label(deal.State)}.");
        }

        Transition(deal, DealState.Progress, actor.Id,
            string.IsNullOrWhiteSpace(note) ? "Transfer started." : note.Trim());

        await _db.SaveChangesAsync(ct);
        await FanOutStateChangeAsync(deal, actor, NotificationKinds.DealStateChanged,
            $"{actor.Name} marked {deal.Reference} as in Progress",
            "Money or material has started moving on this deal.", ct);

        return await BuildDetailAsync(deal, actor, ct);
    }

    public async Task<DealDetailDto> ConfirmSettlementAsync(Profile actor, Guid dealId,
        CancellationToken ct = default)
    {
        var deal = await RequireDealAsync(dealId, actor.Id, ct);
        RequireNotFrozen(deal);

        if (deal.State != DealState.Progress)
        {
            throw new DomainException(
                $"Settlement can only be confirmed on a deal in Progress. This deal is {Label(deal.State)}.");
        }

        var iAmLender = deal.LenderProfileId == actor.Id;

        if (iAmLender)
        {
            if (deal.LenderConfirmedSettlement)
            {
                throw new DomainException("You have already confirmed settlement. Waiting on the other party.");
            }

            deal.LenderConfirmedSettlement = true;
        }
        else
        {
            if (deal.SeekerConfirmedSettlement)
            {
                throw new DomainException("You have already confirmed settlement. Waiting on the other party.");
            }

            deal.SeekerConfirmedSettlement = true;
        }

        var otherId = iAmLender ? deal.SeekerProfileId : deal.LenderProfileId;

        // Completed needs both parties to confirm obligations are cleared (spec, Deal lifecycle).
        if (deal is { LenderConfirmedSettlement: true, SeekerConfirmedSettlement: true })
        {
            Transition(deal, DealState.Completed, actor.Id, "Both parties confirmed settlement.");
            deal.ClosedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);

            foreach (var party in new[] { deal.LenderProfileId, deal.SeekerProfileId })
            {
                await _notifications.PushAsync(party, NotificationKinds.DealCompleted,
                    $"Deal {deal.Reference} is Completed",
                    "Both parties confirmed settlement. Rate your counterparty to close the record.",
                    $"/deals/{deal.Id}", ct);
            }

            await PublishStateAsync(deal, ct);
        }
        else
        {
            _db.DealStateHistories.Add(new DealStateHistory
            {
                Id = Guid.NewGuid(),
                DealId = deal.Id,
                FromState = deal.State,
                ToState = deal.State,
                TriggeredByProfileId = actor.Id,
                Note = $"{actor.Name} confirmed settlement. Waiting on the other party."
            });

            await _db.SaveChangesAsync(ct);

            await _notifications.PushAsync(otherId, NotificationKinds.DealStateChanged,
                $"{actor.Name} confirmed settlement on {deal.Reference}",
                "Confirm from your side to move the deal to Completed.",
                $"/deals/{deal.Id}", ct);
        }

        return await BuildDetailAsync(deal, actor, ct);
    }

    public async Task<DealDetailDto> HaltAsync(Profile actor, Guid dealId, string? reason,
        CancellationToken ct = default)
    {
        var deal = await RequireDealAsync(dealId, actor.Id, ct);
        RequireNotFrozen(deal);

        if (deal.State is not (DealState.Open or DealState.Progress))
        {
            throw new DomainException(
                $"Only an Open or Progress deal can be halted. This deal is {Label(deal.State)}.");
        }

        // The v1 rule: whoever triggers the halt is auto-flagged at fault for that deal, and
        // it counts against their profile (spec, Rating and Trust).
        deal.HaltedByProfileId = actor.Id;
        deal.HaltedAt = DateTimeOffset.UtcNow;
        deal.ClosedAt = DateTimeOffset.UtcNow;

        Transition(deal, DealState.Halted, actor.Id,
            string.IsNullOrWhiteSpace(reason)
                ? $"Halted by {actor.Name}."
                : $"Halted by {actor.Name}: {reason.Trim()}");

        await _db.SaveChangesAsync(ct);

        var otherId = deal.LenderProfileId == actor.Id ? deal.SeekerProfileId : deal.LenderProfileId;

        await _notifications.PushAsync(otherId, NotificationKinds.DealHalted,
            $"{actor.Name} halted deal {deal.Reference}",
            "The deal stays stalled until both parties agree to resume. You can rate this deal now.",
            $"/deals/{deal.Id}", ct);

        await _notifications.PushAsync(actor.Id, NotificationKinds.DealHalted,
            $"You halted deal {deal.Reference}",
            "Because you triggered the halt, this deal is recorded as at fault against your profile.",
            $"/deals/{deal.Id}", ct);

        await PublishStateAsync(deal, ct);

        return await BuildDetailAsync(deal, actor, ct);
    }

    public async Task<ResumeRequestDto> RequestResumeAsync(Profile actor, Guid dealId,
        CancellationToken ct = default)
    {
        var deal = await RequireDealAsync(dealId, actor.Id, ct);
        RequireNotFrozen(deal);

        if (deal.State != DealState.Halted)
        {
            throw new DomainException("Only a halted deal can be resumed.");
        }

        var existing = deal.ResumeRequests
            .FirstOrDefault(r => r.Status == ResumeRequestStatus.Pending);

        if (existing is not null)
        {
            if (existing.RequestedByProfileId == actor.Id)
            {
                throw DomainException.Conflict("You already have a resume request waiting on the other party.");
            }

            throw DomainException.Conflict(
                "The other party has already asked to resume. Accept their request instead.");
        }

        var request = new ResumeRequest
        {
            Id = Guid.NewGuid(),
            DealId = deal.Id,
            RequestedByProfileId = actor.Id
        };

        _db.ResumeRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        var otherId = deal.LenderProfileId == actor.Id ? deal.SeekerProfileId : deal.LenderProfileId;

        await _notifications.PushAsync(otherId, NotificationKinds.ResumeRequested,
            $"{actor.Name} wants to resume {deal.Reference}",
            "A one-sided request changes nothing. Accept it to move the deal back to Progress.",
            $"/deals/{deal.Id}", ct);

        return request.ToDto(actor.Id);
    }

    public async Task<DealDetailDto> RespondToResumeAsync(Profile actor, Guid dealId, Guid requestId,
        bool accept, CancellationToken ct = default)
    {
        var deal = await RequireDealAsync(dealId, actor.Id, ct);
        RequireNotFrozen(deal);

        var request = deal.ResumeRequests.FirstOrDefault(r => r.Id == requestId)
                      ?? throw DomainException.NotFound("That resume request no longer exists.");

        if (request.Status != ResumeRequestStatus.Pending)
        {
            throw new DomainException("That resume request has already been answered.");
        }

        // A one-sided resume request does not change state: only the other party can accept
        // it (spec, History tab).
        if (request.RequestedByProfileId == actor.Id)
        {
            throw DomainException.Forbidden("The other party has to accept your resume request.");
        }

        request.Status = accept ? ResumeRequestStatus.Accepted : ResumeRequestStatus.Declined;
        request.RespondedAt = DateTimeOffset.UtcNow;

        if (accept)
        {
            // Resuming returns the deal to Progress, and clears the closed-out timestamp so
            // it leaves the History tab and reappears among open deals.
            deal.ClosedAt = null;
            deal.HaltedByProfileId = null;
            deal.HaltedAt = null;
            Transition(deal, DealState.Progress, actor.Id, "Both parties agreed to resume.");
        }

        await _db.SaveChangesAsync(ct);

        await _notifications.PushAsync(request.RequestedByProfileId,
            accept ? NotificationKinds.ResumeAccepted : NotificationKinds.DealStateChanged,
            accept
                ? $"{actor.Name} agreed to resume {deal.Reference}"
                : $"{actor.Name} declined resuming {deal.Reference}",
            accept
                ? "The deal is back in Progress."
                : "The deal stays halted.",
            $"/deals/{deal.Id}", ct);

        if (accept)
        {
            await PublishStateAsync(deal, ct);
        }

        return await BuildDetailAsync(deal, actor, ct);
    }

    public async Task<MessageDto> PostMessageAsync(Profile actor, Guid dealId, string body,
        CancellationToken ct = default)
    {
        var deal = await RequireDealAsync(dealId, actor.Id, ct);

        var message = new Message
        {
            Id = Guid.NewGuid(),
            DealId = deal.Id,
            InterestId = deal.InterestId,
            SenderProfileId = actor.Id,
            Body = body.Trim()
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync(ct);

        message.SenderProfile = actor;
        var dto = message.ToDto(actor.Id);
        var otherId = deal.LenderProfileId == actor.Id ? deal.SeekerProfileId : deal.LenderProfileId;

        // Broadcast to the deal group so an open workspace updates live, and to the
        // counterparty's own group so they see it even with the workspace closed.
        await _realtime.ToDealAsync(deal.Id, HubEvents.MessageReceived, dto);
        await _realtime.ToProfileAsync(otherId, HubEvents.MessageReceived, dto);

        return dto;
    }

    // ---- internals -------------------------------------------------------------------

    private static string Label(DealState state) => state switch
    {
        DealState.Open => "Open",
        DealState.Progress => "in Progress",
        DealState.Halted => "Halted",
        DealState.Completed => "Completed",
        _ => state.ToString()
    };

    private static void RequireUnlocked(Profile actor)
    {
        if (actor.VerificationStatus != VerificationStatus.Active)
        {
            throw DomainException.Forbidden(
                "Your account is still being verified. Deals unlock once an administrator approves it.");
        }

        if (actor.IsFrozen)
        {
            throw DomainException.Forbidden("Your account cannot act on the platform right now.");
        }
    }

    /// <summary>
    /// A Suspended or Removed party's in-flight deals are frozen: no further state
    /// transitions until the suspension lifts (spec, Admin role and moderation).
    /// </summary>
    private static void RequireNotFrozen(Deal deal)
    {
        if (deal.LenderProfile.IsFrozen || deal.SeekerProfile.IsFrozen)
        {
            var who = deal.LenderProfile.IsFrozen ? deal.LenderProfile.Name : deal.SeekerProfile.Name;
            throw DomainException.Forbidden(
                $"This deal is frozen while {who}'s account is restricted by an administrator.");
        }
    }

    /// <summary>
    /// Tells the counterparty and any open workspace about a transition. Both channels fire
    /// because a vendor on patchy data may have the workspace closed.
    /// </summary>
    private async Task FanOutStateChangeAsync(Deal deal, Profile actor, string kind, string title,
        string body, CancellationToken ct)
    {
        var otherId = deal.LenderProfileId == actor.Id ? deal.SeekerProfileId : deal.LenderProfileId;

        await _notifications.PushAsync(otherId, kind, title, body, $"/deals/{deal.Id}", ct);
        await PublishStateAsync(deal, ct);
    }

    /// <summary>
    /// Announces a transition on both channels: the deal group reaches an open
    /// workspace, and each party's own group reaches them wherever they are in the
    /// app, so a dashboard or deal list updates without a refresh too.
    /// </summary>
    private async Task PublishStateAsync(Deal deal, CancellationToken ct = default)
    {
        var payload = new { dealId = deal.Id, state = deal.State };

        await _realtime.ToDealAsync(deal.Id, HubEvents.DealStateChanged, payload);
        await _realtime.ToProfileAsync(deal.LenderProfileId, HubEvents.DealStateChanged, payload);
        await _realtime.ToProfileAsync(deal.SeekerProfileId, HubEvents.DealStateChanged, payload);
    }

    private void Transition(Deal deal, DealState to, Guid? actorProfileId, string? note)
    {
        _db.DealStateHistories.Add(new DealStateHistory
        {
            Id = Guid.NewGuid(),
            DealId = deal.Id,
            FromState = deal.State,
            ToState = to,
            TriggeredByProfileId = actorProfileId,
            Note = note
        });

        deal.State = to;
    }

    private async Task<Deal> RequireDealAsync(Guid dealId, Guid profileId, CancellationToken ct)
    {
        var deal = await DealGraph.FirstOrDefaultAsync(d => d.Id == dealId, ct)
                   ?? throw DomainException.NotFound("That deal does not exist.");

        if (deal.LenderProfileId != profileId && deal.SeekerProfileId != profileId)
        {
            throw DomainException.Forbidden("This deal belongs to two other parties.");
        }

        return deal;
    }

    private async Task<CategorySubType?> ResolveSubTypeAsync(DealCategory category, int? subTypeId,
        CancellationToken ct)
    {
        if (subTypeId is null)
        {
            return null;
        }

        var subType = await _db.CategorySubTypes.FirstOrDefaultAsync(s => s.Id == subTypeId, ct)
                      ?? throw new DomainException("That category sub-type does not exist.");

        if (subType.Category != category)
        {
            throw new DomainException($"\"{subType.DisplayName}\" is not a sub-type of {category}.");
        }

        return subType;
    }

    /// <summary>Human-quotable reference, so a vendor can read a deal number out over the phone.</summary>
    private async Task<string> NextReferenceAsync(CancellationToken ct)
    {
        var count = await _db.Deals.CountAsync(ct);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = $"SK-{10000 + count + attempt}";
            if (!await _db.Deals.AnyAsync(d => d.Reference == candidate, ct))
            {
                return candidate;
            }
        }

        return $"SK-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }

    private async Task<DealRowDto> LoadRowAsync(Guid dealId, Guid viewerId, CancellationToken ct)
    {
        var deal = await DealGraph.FirstAsync(d => d.Id == dealId, ct);
        var rows = await ToRowsAsync([deal], viewerId, ct);
        return rows[0];
    }

    private async Task<IReadOnlyList<DealRowDto>> ToRowsAsync(IReadOnlyList<Deal> deals, Guid viewerId,
        CancellationToken ct)
    {
        var counterpartyIds = deals
            .Select(d => d.LenderProfileId == viewerId ? d.SeekerProfileId : d.LenderProfileId)
            .Distinct()
            .ToArray();

        var trust = await _trust.ForAsync(counterpartyIds, ct);

        return deals.Select(d =>
        {
            var otherId = d.LenderProfileId == viewerId ? d.SeekerProfileId : d.LenderProfileId;
            var stats = trust.TryGetValue(otherId, out var t) ? t : TrustStatsService.Empty;
            return d.ToRow(viewerId, stats);
        }).ToList();
    }

    private async Task<DealDetailDto> BuildDetailAsync(Deal deal, Profile actor, CancellationToken ct)
    {
        var timeline = await _db.DealStateHistories.AsNoTracking()
            .Include(h => h.TriggeredByProfile)
            .Where(h => h.DealId == deal.Id)
            .OrderBy(h => h.OccurredAt)
            .ToListAsync(ct);

        var messages = await _db.Messages.AsNoTracking()
            .Include(m => m.SenderProfile)
            .Where(m => m.DealId == deal.Id)
            .OrderBy(m => m.SentAt)
            .ToListAsync(ct);

        var row = (await ToRowsAsync([deal], actor.Id, ct))[0];

        return new DealDetailDto(
            row,
            timeline.Select(DealMappers.ToDto).ToList(),
            messages.Select(m => m.ToDto(actor.Id)).ToList(),
            deal.Ratings.Select(DealMappers.ToDto).ToList(),
            AllowedTransitions(deal, actor));
    }

    /// <summary>
    /// What the logged-in party may trigger right now. Mirrors the state machine exactly so
    /// the UI never offers an action the API will reject.
    /// </summary>
    private static IReadOnlyList<DealState> AllowedTransitions(Deal deal, Profile actor)
    {
        if (deal.LenderProfile.IsFrozen || deal.SeekerProfile.IsFrozen || actor.IsFrozen)
        {
            return [];
        }

        return deal.State switch
        {
            DealState.Open => [DealState.Progress, DealState.Halted],
            // Completed is offered as the settlement confirmation; it only lands once both
            // parties have confirmed.
            DealState.Progress => [DealState.Completed, DealState.Halted],
            // Resuming a Halted deal needs the other party's agreement, so it is modelled as
            // a resume request rather than a direct transition.
            DealState.Halted => [],
            _ => []
        };
    }
}
