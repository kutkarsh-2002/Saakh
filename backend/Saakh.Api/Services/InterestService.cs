using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Mapping;
using Saakh.Api.Hubs;

namespace Saakh.Api.Services;

public interface IInterestService
{
    Task<InterestDto> SendAsync(Profile actor, SendInterestDto dto, CancellationToken ct = default);

    Task<InterestDto> RespondAsync(Profile actor, Guid interestId, bool accept, CancellationToken ct = default);

    Task<IReadOnlyList<InterestDto>> ListAsync(Profile actor, string box, CancellationToken ct = default);

    Task<InterestDto> GetAsync(Profile actor, Guid interestId, CancellationToken ct = default);

    Task<IReadOnlyList<MessageDto>> MessagesAsync(Profile actor, Guid interestId, CancellationToken ct = default);

    Task<MessageDto> PostMessageAsync(Profile actor, Guid interestId, string body, CancellationToken ct = default);
}

public class InterestService : IInterestService
{
    private readonly SaakhDbContext _db;
    private readonly ITrustStatsService _trust;
    private readonly INotificationService _notifications;
    private readonly IRealtimePublisher _realtime;

    public InterestService(SaakhDbContext db, ITrustStatsService trust,
        INotificationService notifications, IRealtimePublisher realtime)
    {
        _db = db;
        _trust = trust;
        _notifications = notifications;
        _realtime = realtime;
    }

    private IQueryable<Interest> Graph => _db.Interests
        .Include(i => i.FromProfile).ThenInclude(p => p.CategorySubType)
        .Include(i => i.ToProfile).ThenInclude(p => p.CategorySubType)
        .Include(i => i.Deals);

    public async Task<InterestDto> SendAsync(Profile actor, SendInterestDto dto, CancellationToken ct = default)
    {
        if (actor.VerificationStatus != VerificationStatus.Active || actor.IsFrozen)
        {
            throw DomainException.Forbidden(
                "Your account needs to be verified and active before you can send interest.");
        }

        if (dto.ToProfileId == actor.Id)
        {
            throw new DomainException("You cannot send interest to your own profile.");
        }

        var target = await _db.Profiles
                         .Include(p => p.CategorySubType)
                         .FirstOrDefaultAsync(p => p.Id == dto.ToProfileId, ct)
                     ?? throw DomainException.NotFound("That profile does not exist.");

        if (target.Role == actor.Role)
        {
            throw new DomainException("Interest only travels between a Lender and a Seeker.");
        }

        // An Inactive or Suspended profile cannot receive new Interest requests (spec, Profile).
        if (!target.IsDiscoverable)
        {
            throw new DomainException($"{target.Name} is not accepting new interest right now.");
        }

        var existing = await Graph.FirstOrDefaultAsync(
            i => i.FromProfileId == actor.Id && i.ToProfileId == target.Id, ct);

        var inbound = await Graph.FirstOrDefaultAsync(
            i => i.FromProfileId == target.Id && i.ToProfileId == actor.Id, ct);

        if (inbound is { Status: InterestStatus.Sent })
        {
            throw DomainException.Conflict(
                $"{target.Name} has already sent you interest. Accept it to open the chat.");
        }

        if (existing is not null)
        {
            if (existing.Status == InterestStatus.Sent)
            {
                throw DomainException.Conflict("You have already sent interest to this profile.");
            }

            if (existing.Status == InterestStatus.Accepted)
            {
                throw DomainException.Conflict("Your interest was already accepted. The chat is open.");
            }

            // A declined signal can be sent again: the row is reused so one pair never
            // accumulates a pile of dead interest rows.
            existing.Status = InterestStatus.Sent;
            existing.Note = dto.Note?.Trim();
            existing.CreatedAt = DateTimeOffset.UtcNow;
            existing.RespondedAt = null;
        }
        else
        {
            existing = new Interest
            {
                Id = Guid.NewGuid(),
                FromProfileId = actor.Id,
                ToProfileId = target.Id,
                Status = InterestStatus.Sent,
                Note = dto.Note?.Trim()
            };
            _db.Interests.Add(existing);
        }

        await _db.SaveChangesAsync(ct);

        await _notifications.PushAsync(target.Id, NotificationKinds.InterestReceived,
            $"{actor.Name} is interested in working with you",
            actor.Role == ProfileRole.Lender
                ? $"A Lender in {actor.District} wants to explore business. Accept to open the chat."
                : $"A Seeker in {actor.District} wants to explore business. Accept to open the chat.",
            "/interests", ct);

        await _realtime.ToProfileAsync(target.Id, HubEvents.InterestChanged,
            new { interestId = existing.Id, status = InterestStatus.Sent });

        return await BuildAsync(existing.Id, actor, ct);
    }

    public async Task<InterestDto> RespondAsync(Profile actor, Guid interestId, bool accept,
        CancellationToken ct = default)
    {
        var interest = await Graph.FirstOrDefaultAsync(i => i.Id == interestId, ct)
                       ?? throw DomainException.NotFound("That interest does not exist.");

        if (interest.ToProfileId != actor.Id)
        {
            throw DomainException.Forbidden("Only the recipient can respond to an interest.");
        }

        if (interest.Status != InterestStatus.Sent)
        {
            throw new DomainException("That interest has already been answered.");
        }

        interest.Status = accept ? InterestStatus.Accepted : InterestStatus.Declined;
        interest.RespondedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        // A decline carries no rating impact (spec, Interest flow) - nothing else to record.
        await _notifications.PushAsync(interest.FromProfileId,
            accept ? NotificationKinds.InterestAccepted : NotificationKinds.InterestDeclined,
            accept
                ? $"{actor.Name} accepted your interest"
                : $"{actor.Name} declined your interest",
            accept
                ? "Chat is open. Negotiate terms, then raise a ticket to create the deal."
                : "No deal was created and nothing is recorded against either profile.",
            accept ? $"/interests/{interest.Id}" : "/interests", ct);

        await _realtime.ToProfileAsync(interest.FromProfileId, HubEvents.InterestChanged,
            new { interestId = interest.Id, status = interest.Status });

        return await BuildAsync(interest.Id, actor, ct);
    }

    public async Task<IReadOnlyList<InterestDto>> ListAsync(Profile actor, string box,
        CancellationToken ct = default)
    {
        var query = box.ToLowerInvariant() switch
        {
            "sent" => Graph.Where(i => i.FromProfileId == actor.Id),
            "received" => Graph.Where(i => i.ToProfileId == actor.Id),
            _ => Graph.Where(i => i.FromProfileId == actor.Id || i.ToProfileId == actor.Id)
        };

        var rows = await query.OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
        return await BuildManyAsync(rows, actor, ct);
    }

    public async Task<InterestDto> GetAsync(Profile actor, Guid interestId, CancellationToken ct = default)
    {
        await RequireParticipantAsync(interestId, actor.Id, ct);
        return await BuildAsync(interestId, actor, ct);
    }

    public async Task<IReadOnlyList<MessageDto>> MessagesAsync(Profile actor, Guid interestId,
        CancellationToken ct = default)
    {
        var interest = await RequireParticipantAsync(interestId, actor.Id, ct);
        RequireChatUnlocked(interest);

        var messages = await _db.Messages.AsNoTracking()
            .Include(m => m.SenderProfile)
            .Where(m => m.InterestId == interestId)
            .OrderBy(m => m.SentAt)
            .ToListAsync(ct);

        // Mark the other party's messages as read, so the unread badge clears on open.
        // The timestamp is hoisted into a local: ExecuteUpdate cannot translate a
        // method call inside SetProperty, only a captured value.
        var readAt = DateTimeOffset.UtcNow;

        await _db.Messages
            .Where(m => m.InterestId == interestId && m.SenderProfileId != actor.Id && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, readAt), ct);

        return messages.Select(m => m.ToDto(actor.Id)).ToList();
    }

    public async Task<MessageDto> PostMessageAsync(Profile actor, Guid interestId, string body,
        CancellationToken ct = default)
    {
        var interest = await RequireParticipantAsync(interestId, actor.Id, ct);
        RequireChatUnlocked(interest);

        var message = new Message
        {
            Id = Guid.NewGuid(),
            InterestId = interestId,
            // Once a ticket exists, the thread continues inside the deal workspace.
            DealId = interest.Deals.OrderByDescending(d => d.CreatedAt).FirstOrDefault()?.Id,
            SenderProfileId = actor.Id,
            Body = body.Trim()
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync(ct);

        message.SenderProfile = actor;
        var dto = message.ToDto(actor.Id);

        var otherId = interest.FromProfileId == actor.Id ? interest.ToProfileId : interest.FromProfileId;
        await _realtime.ToProfileAsync(otherId, HubEvents.MessageReceived, dto);

        if (message.DealId is not null)
        {
            await _realtime.ToDealAsync(message.DealId.Value, HubEvents.MessageReceived, dto);
        }

        return dto;
    }

    // ---- internals -------------------------------------------------------------------

    /// <summary>Chat is unlocked only once interest is accepted (spec, Key concepts).</summary>
    private static void RequireChatUnlocked(Interest interest)
    {
        if (!interest.ChatUnlocked)
        {
            throw DomainException.Forbidden(
                "Chat opens once the interest is accepted.");
        }
    }

    private async Task<Interest> RequireParticipantAsync(Guid interestId, Guid profileId, CancellationToken ct)
    {
        var interest = await Graph.FirstOrDefaultAsync(i => i.Id == interestId, ct)
                       ?? throw DomainException.NotFound("That interest does not exist.");

        if (interest.FromProfileId != profileId && interest.ToProfileId != profileId)
        {
            throw DomainException.Forbidden("You are not part of that conversation.");
        }

        return interest;
    }

    private async Task<InterestDto> BuildAsync(Guid interestId, Profile actor, CancellationToken ct)
    {
        var interest = await Graph.FirstAsync(i => i.Id == interestId, ct);
        return (await BuildManyAsync([interest], actor, ct))[0];
    }

    private async Task<IReadOnlyList<InterestDto>> BuildManyAsync(IReadOnlyList<Interest> rows, Profile actor,
        CancellationToken ct)
    {
        var counterpartyIds = rows
            .Select(i => i.FromProfileId == actor.Id ? i.ToProfileId : i.FromProfileId)
            .Distinct()
            .ToArray();

        var trust = await _trust.ForAsync(counterpartyIds, ct);

        var ids = rows.Select(r => r.Id).ToArray();
        var unread = await _db.Messages.AsNoTracking()
            .Where(m => m.InterestId != null
                        && ids.Contains(m.InterestId.Value)
                        && m.SenderProfileId != actor.Id
                        && m.ReadAt == null)
            .GroupBy(m => m.InterestId!.Value)
            .Select(g => new { InterestId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.Select(i =>
        {
            var sentByMe = i.FromProfileId == actor.Id;
            var counterparty = sentByMe ? i.ToProfile : i.FromProfile;
            var stats = trust.TryGetValue(counterparty.Id, out var t) ? t : TrustStatsService.Empty;

            return new InterestDto(
                i.Id,
                counterparty.ToSummary(stats),
                sentByMe,
                i.Status,
                i.Note,
                i.CreatedAt,
                i.RespondedAt,
                i.ChatUnlocked,
                i.Deals.OrderByDescending(d => d.CreatedAt).FirstOrDefault()?.Id,
                unread.FirstOrDefault(u => u.InterestId == i.Id)?.Count ?? 0);
        }).ToList();
    }
}
