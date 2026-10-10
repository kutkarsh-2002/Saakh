using System.Net;
using FluentAssertions;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Xunit;

namespace Saakh.Tests.Integration;

/// <summary>
/// The access gate from the spec: a no-GSTIN account can reach nothing but its
/// own Profile until an administrator approves it.
///
/// This is tested at the API rather than through the Angular guards on purpose.
/// Hiding a tab is a convenience; the boundary only means something if a typed
/// URL is refused too.
/// </summary>
[Collection(ApiCollection.Name)]
public class VerificationGateTests
{
    private readonly SaakhApiFactory _factory;

    public VerificationGateTests(SaakhApiFactory factory) => _factory = factory;

    public static TheoryData<string> LockedEndpoints() =>
    [
        "/api/opportunities",
        "/api/opportunities/analytics",
        "/api/opportunities/locations",
        "/api/opportunities/open-deals",
        "/api/deals/open",
        "/api/deals/history",
        "/api/interests"
    ];

    [Theory]
    [MemberData(nameof(LockedEndpoints))]
    public async Task An_unverified_account_is_refused_everywhere_but_its_own_profile(string path)
    {
        var account = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Unverified Vendor");

        account.Session!.Profile!.VerificationStatus.Should().Be(VerificationStatus.NeedsApproval);

        var (status, _) = await account.GetAsync<object>(path);

        status.Should().Be(HttpStatusCode.Forbidden, $"{path} must stay locked");
    }

    [Fact]
    public async Task An_unverified_account_can_still_reach_its_own_profile_and_banner()
    {
        var account = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Unverified Vendor");

        var (profileStatus, profile) = await account.GetAsync<ProfileSummaryDto>("/api/profiles/me");
        profileStatus.Should().Be(HttpStatusCode.OK);
        profile!.VerificationStatus.Should().Be(VerificationStatus.NeedsApproval);

        var (bannerStatus, banner) = await account.GetAsync<VerificationStateDto>(
            "/api/profiles/me/verification");
        bannerStatus.Should().Be(HttpStatusCode.OK);
        banner!.FeaturesUnlocked.Should().BeFalse();
    }

    [Fact]
    public async Task A_verified_gstin_opens_the_account_immediately()
    {
        var account = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Registered Business", ApiClient.ValidGstin());

        account.Session!.Profile!.VerificationStatus.Should().Be(VerificationStatus.Active);

        var (status, _) = await account.GetAsync<PagedResultDto<OpportunityRowDto>>("/api/opportunities");
        status.Should().Be(HttpStatusCode.OK, "nothing is locked on the GSTIN path");
    }

    [Fact]
    public async Task An_invalid_gstin_blocks_account_creation_rather_than_degrading_to_review()
    {
        var client = new ApiClient(_factory.CreateClient());
        var phone = $"9{Random.Shared.NextInt64(100000000, 999999999)}";

        // The 00 prefix is the mock's unregistered-number rehearsal.
        var status = await client.PostStatusAsync("/api/auth/register", new
        {
            email = $"{Guid.NewGuid():N}@saakh.test",
            password = "Test@Password1",
            fullName = "Bad GSTIN Business",
            role = ProfileRole.Lender,
            phone,
            gstin = "00AAPFU0939F1ZV",
            isBusiness = true,
            businessSize = BusinessSize.Small,
            country = "India",
            state = "Maharashtra",
            district = "Pune",
            category = DealCategory.RawMaterial,
            categorySubTypeId = 10,
            capacityMin = 100,
            capacityMax = 1000,
            capacityUnit = "kg",
            ownTradeDescription = (string?)null
        });

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unauthenticated_callers_get_401_not_a_silent_empty_list()
    {
        var anonymous = new ApiClient(_factory.CreateClient());

        var (status, _) = await anonymous.GetAsync<object>("/api/opportunities");

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_taxonomy_is_public_because_the_signup_form_needs_it_before_login()
    {
        var anonymous = new ApiClient(_factory.CreateClient());

        var (status, taxonomy) = await anonymous.GetAsync<List<CategorySubTypeDto>>(
            "/api/profiles/taxonomy");

        status.Should().Be(HttpStatusCode.OK);
        taxonomy!.Should().NotBeEmpty();
    }
}
