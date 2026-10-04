using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saakh.Api.Dtos;
using Saakh.Api.Services;

namespace Saakh.Api.Controllers;

/// <summary>
/// The Admin console. Admins sit outside the Lender/Seeker model, hold no discovery profile
/// of their own, and never appear in search.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = SaakhRoles.Admin)]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;
    private readonly ICurrentUser _currentUser;

    public AdminController(IAdminService admin, ICurrentUser currentUser)
    {
        _admin = admin;
        _currentUser = currentUser;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<AdminOverviewDto>> Overview(CancellationToken ct)
        => Ok(await _admin.OverviewAsync(ct));

    /// <summary>User directory: every registered Profile, filterable by region, category, status and type.</summary>
    [HttpGet("profiles")]
    public async Task<ActionResult<PagedResultDto<ProfileSummaryDto>>> Directory(
        [FromQuery] AdminDirectoryQuery query, CancellationToken ct)
        => Ok(await _admin.DirectoryAsync(query, ct));

    [HttpGet("profiles/{id:guid}")]
    public async Task<ActionResult<AdminProfileDetailDto>> ProfileDetail(Guid id, CancellationToken ct)
        => Ok(await _admin.ProfileDetailAsync(id, ct));

    /// <summary>Verification queue: no-GSTIN accounts awaiting the human checkpoint.</summary>
    [HttpGet("verification-queue")]
    public async Task<ActionResult<IReadOnlyList<VerificationQueueRowDto>>> VerificationQueue(
        CancellationToken ct)
        => Ok(await _admin.VerificationQueueAsync(ct));

    /// <summary>
    /// Approve (status becomes Active and the approval email is queued) or reject (status
    /// becomes Rejected, with a reason shown to the user so they can resubmit).
    /// </summary>
    [HttpPost("profiles/{id:guid}/verification")]
    public async Task<ActionResult<ProfileSummaryDto>> ReviewVerification(Guid id,
        ReviewVerificationDto dto, CancellationToken ct)
        => Ok(await _admin.ReviewVerificationAsync(await _currentUser.RequireAdminAsync(ct), id, dto, ct));

    /// <summary>Warning, Suspend (1 week / 2 weeks / 1 month / permanent), Remove, or lift a suspension.</summary>
    [HttpPost("profiles/{id:guid}/moderate")]
    public async Task<ActionResult<ProfileSummaryDto>> Moderate(Guid id, ModerationActionDto dto,
        CancellationToken ct)
        => Ok(await _admin.ModerateAsync(await _currentUser.RequireAdminAsync(ct), id, dto, ct));

    /// <summary>Every moderation and verification decision, with actor and timestamp.</summary>
    [HttpGet("action-log")]
    public async Task<ActionResult<IReadOnlyList<AdminActionLogDto>>> ActionLog([FromQuery] Guid? profileId,
        CancellationToken ct)
        => Ok(await _admin.ActionLogAsync(profileId, ct));

    /// <summary>Streams a submitted evidence document for review.</summary>
    [HttpGet("evidence/{id:guid}")]
    public async Task<IActionResult> Evidence(Guid id, CancellationToken ct)
    {
        var (content, contentType, fileName) = await _admin.OpenEvidenceAsync(id, ct);
        // Inline so an Admin can eyeball a licence photo without downloading it first.
        Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
        return File(content, contentType);
    }
}
