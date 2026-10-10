using System.Net;
using FluentAssertions;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Xunit;

namespace Saakh.Tests.Integration;

/// <summary>
/// Discovery is the mechanism that makes trust portable, and its exclusion rules
/// are the ones with real consequences: a profile that should be hidden but is
/// not can still receive interest it cannot act on.
/// </summary>
[Collection(ApiCollection.Name)]
public class DiscoveryTests
{
    private readonly SaakhApiFactory _factory;

    public DiscoveryTests(SaakhApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_lender_discovers_seekers_and_never_other_lenders()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Viewing Lender", ApiClient.ValidGstin());
        await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Visible Seeker", ApiClient.ValidGstin());
        await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Other Lender", ApiClient.ValidGstin());

        var (status, page) = await lender.GetAsync<PagedResultDto<OpportunityRowDto>>(
            "/api/opportunities?pageSize=100");

        status.Should().Be(HttpStatusCode.OK);
        page!.Items.Should().NotBeEmpty();
        page.Items.Should().OnlyContain(r => r.Profile.Role == ProfileRole.Seeker);
    }

    [Fact]
    public async Task A_seeker_discovers_lenders_mirroring_the_same_view()
    {
        var seeker = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Viewing Seeker", ApiClient.ValidGstin());
        await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Visible Lender", ApiClient.ValidGstin());

        var (_, page) = await seeker.GetAsync<PagedResultDto<OpportunityRowDto>>(
            "/api/opportunities?pageSize=100");

        page!.Items.Should().OnlyContain(r => r.Profile.Role == ProfileRole.Lender);
    }

    [Fact]
    public async Task Only_verified_and_available_profiles_appear()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Viewing Lender", ApiClient.ValidGstin());

        // An unverified seeker exists, but must not be discoverable.
        var unverified = await ApiClient.RegisterAsync(_factory, ProfileRole.Seeker, "Unverified Seeker");

        var (_, page) = await lender.GetAsync<PagedResultDto<OpportunityRowDto>>(
            "/api/opportunities?pageSize=100");

        page!.Items.Should().NotContain(r => r.Profile.Id == unverified.ProfileId);
        page.Items.Should().OnlyContain(r =>
            r.Profile.VerificationStatus == VerificationStatus.Active &&
            r.Profile.AvailabilityStatus == AvailabilityStatus.Active);
    }

    [Fact]
    public async Task Going_inactive_removes_a_profile_from_search_and_stops_new_interest()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Viewing Lender", ApiClient.ValidGstin());
        var seeker = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Going Quiet", ApiClient.ValidGstin());

        var (_, before) = await lender.GetAsync<PagedResultDto<OpportunityRowDto>>(
            "/api/opportunities?pageSize=100");
        before!.Items.Should().Contain(r => r.Profile.Id == seeker.ProfileId);

        await seeker.PutAsync<ProfileSummaryDto>("/api/profiles/me/availability", new { active = false });

        var (_, after) = await lender.GetAsync<PagedResultDto<OpportunityRowDto>>(
            "/api/opportunities?pageSize=100");
        after!.Items.Should().NotContain(r => r.Profile.Id == seeker.ProfileId);

        var interest = await lender.PostStatusAsync(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });
        interest.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Inactive_is_reversible_and_in_flight_deals_are_untouched_by_it()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Lender", ApiClient.ValidGstin());
        var seeker = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Seeker", ApiClient.ValidGstin());

        var interest = await lender.PostOkAsync<InterestDto>(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });
        await seeker.PostAsync<InterestDto>($"/api/interests/{interest.Id}/respond", new { accept = true });
        await ApiClient.OpenDealAsync(lender, seeker, interest.Id, new
        {
            interestId = interest.Id,
            category = DealCategory.RawMaterial,
            categorySubTypeId = 10,
            capacity = 200,
            capacityUnit = "kg",
            materialDescription = "Oranges",
            description = "In flight.",
            estimatedSettlementTime = DateTimeOffset.UtcNow.AddDays(30)
        });

        await seeker.PutAsync<ProfileSummaryDto>("/api/profiles/me/availability", new { active = false });

        var (_, stillOpen) = await seeker.GetAsync<List<DealRowDto>>("/api/deals/open");
        stillOpen!.Should().HaveCount(1, "an inactive profile keeps running its existing deals");

        var (_, reactivated) = await seeker.PutAsync<ProfileSummaryDto>(
            "/api/profiles/me/availability", new { active = true });
        reactivated!.AvailabilityStatus.Should().Be(AvailabilityStatus.Active);
    }

    [Fact]
    public async Task Interest_cannot_be_sent_to_oneself_or_to_the_same_side_of_the_market()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Lender", ApiClient.ValidGstin());
        var otherLender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Other Lender", ApiClient.ValidGstin());

        var toSelf = await lender.PostStatusAsync(
            "/api/interests", new { toProfileId = lender.ProfileId, note = (string?)null });
        toSelf.Should().Be(HttpStatusCode.BadRequest);

        var toSameRole = await lender.PostStatusAsync(
            "/api/interests", new { toProfileId = otherLender.ProfileId, note = (string?)null });
        toSameRole.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Duplicate_interest_to_the_same_profile_is_refused()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Lender", ApiClient.ValidGstin());
        var seeker = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Seeker", ApiClient.ValidGstin());

        var first = await lender.PostStatusAsync(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });
        first.Should().Be(HttpStatusCode.OK);

        var second = await lender.PostStatusAsync(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });
        second.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Default_ranking_returns_candidates_ordered_by_match_score()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Ranking Lender", ApiClient.ValidGstin());

        // A near, same-category seeker should outrank a distant, different one.
        await ApiClient.RegisterAsync(_factory, ProfileRole.Seeker, "Near Match",
            ApiClient.ValidGstin(), state: "Maharashtra", district: "Pune", subTypeId: 10);
        await ApiClient.RegisterAsync(_factory, ProfileRole.Seeker, "Far Different",
            ApiClient.ValidGstin(), state: "West Bengal", district: "Kolkata",
            category: DealCategory.Money, subTypeId: 1);

        var (_, page) = await lender.GetAsync<PagedResultDto<OpportunityRowDto>>(
            "/api/opportunities?pageSize=100");

        var scores = page!.Items.Select(i => i.MatchScore).ToList();
        scores.Should().BeInDescendingOrder();

        var near = page.Items.First(i => i.Profile.Name == "Near Match");
        var far = page.Items.First(i => i.Profile.Name == "Far Different");
        near.MatchScore.Should().BeGreaterThan(far.MatchScore);
    }

    [Fact]
    public async Task A_profile_with_no_ratings_is_excluded_once_a_minimum_rating_is_set()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Filtering Lender", ApiClient.ValidGstin());
        var newcomer = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Brand New", ApiClient.ValidGstin());

        var (_, filtered) = await lender.GetAsync<PagedResultDto<OpportunityRowDto>>(
            "/api/opportunities?minRating=4&pageSize=100");

        // Documented behaviour, and the reason the UI warns about this filter.
        filtered!.Items.Should().NotContain(r => r.Profile.Id == newcomer.ProfileId);
    }
}
