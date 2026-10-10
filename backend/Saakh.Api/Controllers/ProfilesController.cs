using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saakh.Api.Domain;
using Saakh.Api.Infrastructure;
using Saakh.Api.Dtos;
using Saakh.Api.Services;

namespace Saakh.Api.Controllers;

[ApiController]
[Route("api/profiles")]
[Authorize(Roles = SaakhRoles.Trader)]
public class ProfilesController : ControllerBase
{
    private readonly IProfileService _profiles;
    private readonly IRatingService _ratings;
    private readonly ICurrentUser _currentUser;

    public ProfilesController(IProfileService profiles, IRatingService ratings, ICurrentUser currentUser)
    {
        _profiles = profiles;
        _ratings = ratings;
        _currentUser = currentUser;
    }

    [HttpGet("me")]
    public async Task<ActionResult<ProfileSummaryDto>> Mine(CancellationToken ct)
        => Ok(await _profiles.MineAsync(await _currentUser.RequireProfileAsync(ct), ct));

    [HttpPut("me")]
    public async Task<ActionResult<ProfileSummaryDto>> Update(UpdateProfileDto dto, CancellationToken ct)
        => Ok(await _profiles.UpdateAsync(await _currentUser.RequireProfileAsync(ct), dto, ct));

    /// <summary>
    /// Self-service Active/Inactive toggle. An Inactive profile leaves search and stops
    /// receiving Interest, but its in-flight deals continue unaffected.
    /// </summary>
    [HttpPut("me/availability")]
    public async Task<ActionResult<ProfileSummaryDto>> SetAvailability(SetAvailabilityDto dto,
        CancellationToken ct)
        => Ok(await _profiles.SetAvailabilityAsync(await _currentUser.RequireProfileAsync(ct), dto.Active, ct));

    /// <summary>
    /// Adds a GSTIN to an account that signed up without one. A number the registry
    /// confirms opens the account on the spot, so a vendor who gets registered after
    /// joining does not have to wait on the manual review queue.
    /// </summary>
    [HttpPost("me/gstin")]
    public async Task<ActionResult<AddGstinResultDto>> AddGstin(AddGstinDto dto, CancellationToken ct)
        => Ok(await _profiles.AddGstinAsync(await _currentUser.RequireProfileAsync(ct), dto.Gstin, ct));

    /// <summary>The four-state verification banner payload.</summary>
    [HttpGet("me/verification")]
    public async Task<ActionResult<VerificationStateDto>> Verification(CancellationToken ct)
        => Ok(await _profiles.VerificationStateAsync(await _currentUser.RequireProfileAsync(ct), ct));

    /// <summary>Submits proof documents, moving a Needs Approval or Rejected account to Pending.</summary>
    [HttpPost("me/verification/evidence")]
    [RequestSizeLimit(40 * 1024 * 1024)]
    public async Task<ActionResult<VerificationStateDto>> SubmitEvidence(
        [FromForm] IFormFileCollection files,
        [FromForm] string? documentType,
        CancellationToken ct)
    {
        if (files is null || files.Count == 0)
        {
            throw new DomainException("Attach at least one proof document.");
        }

        var profile = await _currentUser.RequireProfileAsync(ct);

        var uploads = new List<EvidenceUpload>();
        var streams = new List<Stream>();

        try
        {
            foreach (var file in files)
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                uploads.Add(new EvidenceUpload(file.FileName, file.ContentType, file.Length,
                    documentType ?? "Unspecified", stream));
            }

            return Ok(await _profiles.SubmitEvidenceAsync(profile, uploads, ct));
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    /// <summary>The shared category/sub-type taxonomy, used by every form and filter.</summary>
    [HttpGet("taxonomy")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<CategorySubTypeDto>>> Taxonomy(CancellationToken ct)
        => Ok(await _profiles.TaxonomyAsync(ct));

    // Viewing another party's trust record is a discovery feature, so it sits behind the
    // same verification gate as search and deals.
    [HttpGet("{id:guid}")]
    [RequireVerifiedProfile]
    public async Task<ActionResult<ProfileSummaryDto>> Public(Guid id, CancellationToken ct)
        => Ok(await _profiles.PublicAsync(id, ct));

    [HttpGet("{id:guid}/ratings")]
    [RequireVerifiedProfile]
    public async Task<ActionResult<IReadOnlyList<RatingDto>>> Ratings(Guid id, CancellationToken ct)
        => Ok(await _ratings.ForProfileAsync(id, ct));
}
