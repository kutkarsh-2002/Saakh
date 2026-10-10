using System.Net;
using FluentAssertions;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Xunit;

namespace Saakh.Tests.Integration;

/// <summary>
/// A vendor who registers for GST after joining should not have to queue behind a
/// human reviewer: the registry is the same proof here that it is at signup. The
/// rule that does not bend is the other direction — a verified GSTIN is the identity
/// the trust record is anchored to, so it is added once and never swapped.
/// </summary>
[Collection(ApiCollection.Name)]
public class AddGstinTests
{
    private readonly SaakhApiFactory _factory;

    public AddGstinTests(SaakhApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_verified_gstin_opens_a_locked_account_without_a_reviewer()
    {
        var account = await ApiClient.RegisterAsync(_factory, ProfileRole.Seeker, "Later Registrant");
        account.Session!.Profile!.VerificationStatus.Should().Be(VerificationStatus.NeedsApproval);

        // Locked before: discovery is one of the tabs the gate closes.
        var (before, _) = await account.GetAsync<object>("/api/opportunities");
        before.Should().Be(HttpStatusCode.Forbidden);

        var (status, result) = await account.PostAsync<AddGstinResultDto>(
            "/api/profiles/me/gstin", new { gstin = ApiClient.ValidGstin() });

        status.Should().Be(HttpStatusCode.OK);
        result!.Verified.Should().BeTrue();
        result.Profile.VerificationStatus.Should().Be(VerificationStatus.Active);
        result.Profile.GstinVerified.Should().BeTrue();
        // A verified GSTIN is a business registration, whatever the account said at signup.
        result.Profile.IsBusiness.Should().BeTrue();

        var (after, _) = await account.GetAsync<object>("/api/opportunities");
        after.Should().Be(HttpStatusCode.OK, "the account is Active now, so nothing stays locked");
    }

    [Fact]
    public async Task The_number_is_never_returned_in_full_to_anybody_else()
    {
        var account = await ApiClient.RegisterAsync(_factory, ProfileRole.Seeker, "Masked Registrant");
        var gstin = ApiClient.ValidGstin();

        var (_, result) = await account.PostAsync<AddGstinResultDto>(
            "/api/profiles/me/gstin", new { gstin });

        // Own profile sees it in full; the masking rule is asserted for viewers in the
        // mapper tests. Here the point is that adding it does not change that contract.
        result!.Profile.GstinMasked.Should().Be(gstin);

        var onlooker = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Onlooker", ApiClient.ValidGstin());

        var (status, seen) = await onlooker.GetAsync<ProfileSummaryDto>(
            $"/api/profiles/{result.Profile.Id}");

        status.Should().Be(HttpStatusCode.OK);
        seen!.GstinMasked.Should().NotBe(gstin);
        seen.GstinMasked.Should().Contain("XXXX");
    }

    [Fact]
    public async Task A_verified_gstin_cannot_be_swapped_for_another_one()
    {
        var account = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Already Verified", ApiClient.ValidGstin());

        var (status, _) = await account.PostAsync<object>(
            "/api/profiles/me/gstin", new { gstin = ApiClient.ValidGstin() });

        status.Should().Be(HttpStatusCode.BadRequest,
            "the trust record is anchored to the identity that was verified");
    }

    [Fact]
    public async Task A_malformed_number_is_refused_before_the_registry_is_called()
    {
        var account = await ApiClient.RegisterAsync(_factory, ProfileRole.Seeker, "Typo Vendor");

        var (status, _) = await account.PostAsync<object>(
            "/api/profiles/me/gstin", new { gstin = "NOT-A-GSTIN" });

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_number_the_registry_rejects_leaves_the_account_locked()
    {
        var account = await ApiClient.RegisterAsync(_factory, ProfileRole.Seeker, "Unregistered Vendor");

        // The mock verifier treats a leading 00 as not registered.
        var (status, _) = await account.PostAsync<object>(
            "/api/profiles/me/gstin", new { gstin = "00AAPFU0939F1ZV" });

        status.Should().Be(HttpStatusCode.BadRequest);

        var (_, profile) = await account.GetAsync<ProfileSummaryDto>("/api/profiles/me");
        profile!.VerificationStatus.Should().Be(VerificationStatus.NeedsApproval);
        profile.GstinVerified.Should().BeFalse();
    }

    [Fact]
    public async Task The_same_gstin_cannot_be_verified_on_two_accounts()
    {
        var gstin = ApiClient.ValidGstin();
        await ApiClient.RegisterAsync(_factory, ProfileRole.Lender, "First Claimant", gstin);

        var second = await ApiClient.RegisterAsync(_factory, ProfileRole.Seeker, "Second Claimant");

        var (status, _) = await second.PostAsync<object>("/api/profiles/me/gstin", new { gstin });

        status.Should().Be(HttpStatusCode.BadRequest, "one registration is one identity");
    }
}
