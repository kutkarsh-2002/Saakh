using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saakh.Api.Dtos;
using Saakh.Api.Infrastructure;
using Saakh.Api.Services;

namespace Saakh.Api.Controllers;

[ApiController]
[Route("api/interests")]
[Authorize(Roles = SaakhRoles.Trader)]
[RequireVerifiedProfile]
public class InterestsController : ControllerBase
{
    private readonly IInterestService _interests;
    private readonly ICurrentUser _currentUser;

    public InterestsController(IInterestService interests, ICurrentUser currentUser)
    {
        _interests = interests;
        _currentUser = currentUser;
    }

    /// <summary>"all" (default), "sent", or "received".</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InterestDto>>> List([FromQuery] string box = "all",
        CancellationToken ct = default)
        => Ok(await _interests.ListAsync(await _currentUser.RequireProfileAsync(ct), box, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InterestDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _interests.GetAsync(await _currentUser.RequireProfileAsync(ct), id, ct));

    [HttpPost]
    public async Task<ActionResult<InterestDto>> Send(SendInterestDto dto, CancellationToken ct)
        => Ok(await _interests.SendAsync(await _currentUser.RequireProfileAsync(ct), dto, ct));

    /// <summary>Accepting unlocks the chat; declining creates no deal and no rating impact.</summary>
    [HttpPost("{id:guid}/respond")]
    public async Task<ActionResult<InterestDto>> Respond(Guid id, RespondInterestDto dto, CancellationToken ct)
        => Ok(await _interests.RespondAsync(await _currentUser.RequireProfileAsync(ct), id, dto.Accept, ct));

    /// <summary>Pre-ticket negotiation thread. Only readable once the interest is accepted.</summary>
    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> Messages(Guid id, CancellationToken ct)
        => Ok(await _interests.MessagesAsync(await _currentUser.RequireProfileAsync(ct), id, ct));

    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<MessageDto>> PostMessage(Guid id, SendMessageDto dto, CancellationToken ct)
        => Ok(await _interests.PostMessageAsync(await _currentUser.RequireProfileAsync(ct), id, dto.Body, ct));
}
