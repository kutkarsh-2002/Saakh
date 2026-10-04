using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Saakh.Api.Dtos;
using Saakh.Api.Services;

namespace Saakh.Api.Hubs;

/// <summary>
/// Client-bound event names. Kept in one place so the Angular realtime service and the API
/// cannot drift apart silently.
/// </summary>
public static class HubEvents
{
    public const string Notification = "notification";
    public const string MessageReceived = "messageReceived";
    public const string DealStateChanged = "dealStateChanged";
    public const string InterestChanged = "interestChanged";
    public const string VerificationChanged = "verificationChanged";
    public const string ProfileModerated = "profileModerated";
}

/// <summary>
/// Drives the live status banner, Interest notifications and deal chat (tech-stack.md).
/// Every connection joins a group keyed by its own Profile id, so a server-side push only
/// needs the recipient's profile id and never a connection id.
/// </summary>
[Authorize]
public class SaakhHub : Hub
{
    private readonly ICurrentUser _currentUser;

    public SaakhHub(ICurrentUser currentUser) => _currentUser = currentUser;

    public static string ProfileGroup(Guid profileId) => $"profile:{profileId}";

    public static string DealGroup(Guid dealId) => $"deal:{dealId}";

    public override async Task OnConnectedAsync()
    {
        var profileId = _currentUser.ProfileId;
        if (profileId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ProfileGroup(profileId.Value));
        }

        var adminId = _currentUser.AdminId;
        if (adminId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Called when the deal workspace opens, so chat lands without a page refresh.</summary>
    public Task JoinDeal(Guid dealId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, DealGroup(dealId));

    public Task LeaveDeal(Guid dealId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, DealGroup(dealId));
}

/// <summary>Server-side push surface, so services never take a dependency on IHubContext directly.</summary>
public interface IRealtimePublisher
{
    Task ToProfileAsync(Guid profileId, string eventName, object payload);

    Task ToDealAsync(Guid dealId, string eventName, object payload);

    Task ToAdminsAsync(string eventName, object payload);
}

public class RealtimePublisher : IRealtimePublisher
{
    private readonly IHubContext<SaakhHub> _hub;

    public RealtimePublisher(IHubContext<SaakhHub> hub) => _hub = hub;

    public Task ToProfileAsync(Guid profileId, string eventName, object payload) =>
        _hub.Clients.Group(SaakhHub.ProfileGroup(profileId)).SendAsync(eventName, payload);

    public Task ToDealAsync(Guid dealId, string eventName, object payload) =>
        _hub.Clients.Group(SaakhHub.DealGroup(dealId)).SendAsync(eventName, payload);

    public Task ToAdminsAsync(string eventName, object payload) =>
        _hub.Clients.Group("admins").SendAsync(eventName, payload);
}
