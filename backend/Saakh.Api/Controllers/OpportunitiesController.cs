using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Infrastructure;
using Saakh.Api.Services;

namespace Saakh.Api.Controllers;

/// <summary>
/// The Opportunity dashboard: symmetric discovery (a Lender sees Seekers, a Seeker sees
/// Lenders), the two analytics charts, and the user's own open deals.
/// </summary>
[ApiController]
[Route("api/opportunities")]
[Authorize(Roles = SaakhRoles.Trader)]
[RequireVerifiedProfile]
public class OpportunitiesController : ControllerBase
{
    private readonly IDiscoveryService _discovery;
    private readonly IAnalyticsService _analytics;
    private readonly IDealService _deals;
    private readonly ICurrentUser _currentUser;

    public OpportunitiesController(IDiscoveryService discovery, IAnalyticsService analytics,
        IDealService deals, ICurrentUser currentUser)
    {
        _discovery = discovery;
        _analytics = analytics;
        _deals = deals;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Candidate profiles on the other side of the market. With no filters applied the list
    /// is ranked by the default blend of proximity, category match and rating.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResultDto<OpportunityRowDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] string? country,
        [FromQuery] string[]? states,
        [FromQuery] string[]? districts,
        [FromQuery] DealCategory[]? categories,
        [FromQuery] int[]? subTypeIds,
        [FromQuery] decimal? capacityMin,
        [FromQuery] decimal? capacityMax,
        [FromQuery] double? minRating,
        [FromQuery] string sortBy = "match",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var profile = await _currentUser.RequireProfileAsync(ct);

        var query = new DiscoveryQuery
        {
            Search = search,
            Country = country,
            States = states,
            Districts = districts,
            Categories = categories,
            SubTypeIds = subTypeIds,
            CapacityMin = capacityMin,
            CapacityMax = capacityMax,
            MinRating = minRating,
            SortBy = sortBy,
            Page = page,
            PageSize = pageSize
        };

        return Ok(await _discovery.SearchAsync(profile, query, ct));
    }

    /// <summary>Location options for the filter bar, drawn from profiles that actually exist.</summary>
    [HttpGet("locations")]
    public async Task<ActionResult<IReadOnlyList<LocationOptionDto>>> Locations(CancellationToken ct)
    {
        var profile = await _currentUser.RequireProfileAsync(ct);
        return Ok(await _discovery.LocationOptionsAsync(DiscoveryService.Opposite(profile.Role), ct));
    }

    /// <summary>The two dashboard charts, filtered to the logged-in user's own deals.</summary>
    [HttpGet("analytics")]
    public async Task<ActionResult<DashboardAnalyticsDto>> Analytics([FromQuery] int months = 9,
        CancellationToken ct = default)
    {
        var profile = await _currentUser.RequireProfileAsync(ct);
        return Ok(await _analytics.ForProfileAsync(profile.Id, months, ct));
    }

    /// <summary>
    /// The Open Deals table, distinct from the discovery table above it: the user's own
    /// in-flight Open and Progress deals.
    /// </summary>
    [HttpGet("open-deals")]
    public async Task<ActionResult<IReadOnlyList<DealRowDto>>> OpenDeals(CancellationToken ct)
    {
        var profile = await _currentUser.RequireProfileAsync(ct);
        return Ok(await _deals.OpenDealsAsync(profile, ct));
    }
}
