using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Mapping;
using Saakh.Api.Hubs;

namespace Saakh.Api.Services;

public interface INotificationService
{
    /// <summary>Persists a notification and pushes it to the recipient over SignalR.</summary>
    Task<NotificationDto> PushAsync(Guid profileId, string kind, string title, string body,
        string? link = null, CancellationToken ct = default);

    Task<IReadOnlyList<NotificationDto>> ListAsync(Guid profileId, bool unreadOnly,
        CancellationToken ct = default);

    Task MarkReadAsync(Guid profileId, Guid? notificationId, CancellationToken ct = default);
}

public class NotificationService : INotificationService
{
    private readonly SaakhDbContext _db;
    private readonly IRealtimePublisher _realtime;

    public NotificationService(SaakhDbContext db, IRealtimePublisher realtime)
    {
        _db = db;
        _realtime = realtime;
    }

    public async Task<NotificationDto> PushAsync(Guid profileId, string kind, string title, string body,
        string? link = null, CancellationToken ct = default)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            Kind = kind,
            Title = title,
            Body = body,
            Link = link
        };

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        var dto = notification.ToDto();
        await _realtime.ToProfileAsync(profileId, HubEvents.Notification, dto);
        return dto;
    }

    public async Task<IReadOnlyList<NotificationDto>> ListAsync(Guid profileId, bool unreadOnly,
        CancellationToken ct = default)
    {
        var query = _db.Notifications.AsNoTracking().Where(n => n.ProfileId == profileId);

        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        var rows = await query.OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync(ct);
        return rows.Select(AdminMappers.ToDto).ToList();
    }

    public async Task MarkReadAsync(Guid profileId, Guid? notificationId, CancellationToken ct = default)
    {
        var query = _db.Notifications.Where(n => n.ProfileId == profileId && !n.IsRead);

        if (notificationId is not null)
        {
            query = query.Where(n => n.Id == notificationId);
        }

        await query.ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }
}

/// <summary>Notification kind keys, matched to icons on the client.</summary>
public static class NotificationKinds
{
    public const string InterestReceived = "interest.received";
    public const string InterestAccepted = "interest.accepted";
    public const string InterestDeclined = "interest.declined";
    public const string DealRaised = "deal.raised";
    public const string DealStateChanged = "deal.state";
    public const string DealHalted = "deal.halted";
    public const string DealCompleted = "deal.completed";
    public const string ResumeRequested = "deal.resume.requested";
    public const string ResumeAccepted = "deal.resume.accepted";
    public const string RatingReceived = "rating.received";
    public const string MessageReceived = "message.received";
    public const string VerificationApproved = "verification.approved";
    public const string VerificationRejected = "verification.rejected";
    public const string Moderation = "moderation";
}
