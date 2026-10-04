using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saakh.Api.Dtos;
using Saakh.Api.Services;

namespace Saakh.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = SaakhRoles.Trader)]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;
    private readonly ICurrentUser _currentUser;

    public NotificationsController(INotificationService notifications, ICurrentUser currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Persisted notifications, so a vendor on patchy mobile data still sees what arrived
    /// while they were disconnected from the SignalR hub.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List([FromQuery] bool unreadOnly = false,
        CancellationToken ct = default)
    {
        var profileId = _currentUser.ProfileId
                        ?? throw DomainException.Forbidden("This account has no trading profile.");
        return Ok(await _notifications.ListAsync(profileId, unreadOnly, ct));
    }

    [HttpPost("read")]
    public async Task<IActionResult> MarkRead([FromQuery] Guid? id, CancellationToken ct)
    {
        var profileId = _currentUser.ProfileId
                        ?? throw DomainException.Forbidden("This account has no trading profile.");
        await _notifications.MarkReadAsync(profileId, id, ct);
        return NoContent();
    }
}
