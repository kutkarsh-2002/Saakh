using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Infrastructure;
using Saakh.Api.Services;

namespace Saakh.Api.Controllers;

[ApiController]
[Route("api/deals")]
[Authorize(Roles = SaakhRoles.Trader)]
[RequireVerifiedProfile]
public class DealsController : ControllerBase
{
    private readonly IDealService _deals;
    private readonly IRatingService _ratings;
    private readonly ICurrentUser _currentUser;

    public DealsController(IDealService deals, IRatingService ratings, ICurrentUser currentUser)
    {
        _deals = deals;
        _ratings = ratings;
        _currentUser = currentUser;
    }

    /// <summary>Raising the ticket creates the Deal in Open state, visible to both parties.</summary>
    [HttpPost("tickets")]
    public async Task<ActionResult<DealRowDto>> RaiseTicket(RaiseTicketDto dto, CancellationToken ct)
        => Ok(await _deals.RaiseTicketAsync(await _currentUser.RequireProfileAsync(ct), dto, ct));

    [HttpGet("open")]
    public async Task<ActionResult<IReadOnlyList<DealRowDto>>> Open(CancellationToken ct)
        => Ok(await _deals.OpenDealsAsync(await _currentUser.RequireProfileAsync(ct), ct));

    /// <summary>
    /// History: every deal that has left the active states. <paramref name="state"/> toggles
    /// between Completed and Halted, or is omitted to see both.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<DealRowDto>>> History([FromQuery] DealState? state,
        CancellationToken ct = default)
        => Ok(await _deals.HistoryAsync(await _currentUser.RequireProfileAsync(ct), state, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DealDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _deals.GetAsync(await _currentUser.RequireProfileAsync(ct), id, ct));

    /// <summary>Open to Progress: money or material has started moving.</summary>
    [HttpPost("{id:guid}/progress")]
    public async Task<ActionResult<DealDetailDto>> StartProgress(Guid id, AdvanceDealDto dto,
        CancellationToken ct)
        => Ok(await _deals.StartProgressAsync(await _currentUser.RequireProfileAsync(ct), id, dto.Note, ct));

    /// <summary>
    /// Confirms this party's side of settlement. The deal only reaches Completed once both
    /// parties have confirmed.
    /// </summary>
    [HttpPost("{id:guid}/settle")]
    public async Task<ActionResult<DealDetailDto>> ConfirmSettlement(Guid id, CancellationToken ct)
        => Ok(await _deals.ConfirmSettlementAsync(await _currentUser.RequireProfileAsync(ct), id, ct));

    /// <summary>
    /// Halts the deal. Whoever triggers the halt is auto-flagged at fault for it, which is
    /// the v1 rule and is stated plainly in the confirmation copy on the client.
    /// </summary>
    [HttpPost("{id:guid}/halt")]
    public async Task<ActionResult<DealDetailDto>> Halt(Guid id, HaltDealDto dto, CancellationToken ct)
        => Ok(await _deals.HaltAsync(await _currentUser.RequireProfileAsync(ct), id, dto.Reason, ct));

    /// <summary>Asks the other party to resume a halted deal. One-sided, so it changes no state.</summary>
    [HttpPost("{id:guid}/resume-request")]
    public async Task<ActionResult<ResumeRequestDto>> RequestResume(Guid id, CancellationToken ct)
        => Ok(await _deals.RequestResumeAsync(await _currentUser.RequireProfileAsync(ct), id, ct));

    [HttpPost("{id:guid}/resume-request/{requestId:guid}/respond")]
    public async Task<ActionResult<DealDetailDto>> RespondToResume(Guid id, Guid requestId,
        RespondInterestDto dto, CancellationToken ct)
        => Ok(await _deals.RespondToResumeAsync(await _currentUser.RequireProfileAsync(ct), id, requestId,
            dto.Accept, ct));

    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<MessageDto>> PostMessage(Guid id, SendMessageDto dto, CancellationToken ct)
        => Ok(await _deals.PostMessageAsync(await _currentUser.RequireProfileAsync(ct), id, dto.Body, ct));

    /// <summary>One rating per party per deal, available once the deal is Completed or Halted.</summary>
    [HttpPost("{id:guid}/rating")]
    public async Task<ActionResult<RatingDto>> Rate(Guid id, SubmitRatingDto dto, CancellationToken ct)
        => Ok(await _ratings.SubmitAsync(await _currentUser.RequireProfileAsync(ct), id, dto, ct));
}
