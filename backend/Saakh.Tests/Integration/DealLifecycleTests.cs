using System.Net;
using FluentAssertions;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Xunit;

namespace Saakh.Tests.Integration;

/// <summary>
/// The deal state machine, exercised through the HTTP surface exactly as the
/// Angular client drives it. Every rule here is one the spec calls final, so
/// each test names the rule rather than the endpoint.
/// </summary>
[Collection(ApiCollection.Name)]
public class DealLifecycleTests
{
    private readonly SaakhApiFactory _factory;

    public DealLifecycleTests(SaakhApiFactory factory) => _factory = factory;

    private async Task<(ApiClient Lender, ApiClient Seeker)> PairAsync()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Test Lender", ApiClient.ValidGstin());
        var seeker = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Test Seeker", ApiClient.ValidGstin());
        return (lender, seeker);
    }

    /// <summary>Interest, accepted, then a ticket raised: the path to an Open deal.</summary>
    private async Task<(ApiClient Lender, ApiClient Seeker, DealRowDto Deal)> OpenDealAsync()
    {
        var (lender, seeker) = await PairAsync();

        var interest = await lender.PostOkAsync<InterestDto>(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = "Exploring supply." });

        await seeker.PostAsync<InterestDto>($"/api/interests/{interest.Id}/respond", new { accept = true });

        var deal = await ApiClient.OpenDealAsync(lender, seeker, interest.Id);

        return (lender, seeker, deal);
    }

    [Fact]
    public async Task Chat_is_locked_until_the_interest_is_accepted()
    {
        var (lender, seeker) = await PairAsync();

        var interest = await lender.PostOkAsync<InterestDto>(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });

        var (status, _) = await lender.GetAsync<List<MessageDto>>($"/api/interests/{interest.Id}/messages");

        status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_ticket_cannot_be_raised_before_the_interest_is_accepted()
    {
        var (lender, seeker) = await PairAsync();

        var interest = await lender.PostOkAsync<InterestDto>(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });

        var status = await lender.PostStatusAsync("/api/deals/proposals", new
        {
            interestId = interest.Id,
            category = DealCategory.Money,
            capacity = 50000,
            capacityUnit = "INR",
            description = "Too early.",
            estimatedSettlementTime = DateTimeOffset.UtcNow.AddDays(30)
        });

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_the_recipient_can_accept_an_interest()
    {
        var (lender, seeker) = await PairAsync();

        var interest = await lender.PostOkAsync<InterestDto>(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });

        var status = await lender.PostStatusAsync(
            $"/api/interests/{interest.Id}/respond", new { accept = true });

        status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Raising_a_ticket_opens_the_deal_and_carries_the_chat_across()
    {
        var (lender, seeker) = await PairAsync();

        var interest = await lender.PostOkAsync<InterestDto>(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });
        await seeker.PostAsync<InterestDto>($"/api/interests/{interest.Id}/respond", new { accept = true });

        await lender.PostAsync<MessageDto>(
            $"/api/interests/{interest.Id}/messages", new { body = "30-day terms work?" });
        await seeker.PostAsync<MessageDto>(
            $"/api/interests/{interest.Id}/messages", new { body = "Yes. Raise the ticket." });

        var deal = await ApiClient.OpenDealAsync(lender, seeker, interest.Id);

        deal.DealState.Should().Be(DealState.Open);

        var (_, detail) = await seeker.GetAsync<DealDetailDto>($"/api/deals/{deal.Id}");

        detail!.Messages.Should().HaveCount(2, "the negotiation is part of the deal's record");
        detail.Timeline.Should().ContainSingle().Which.ToState.Should().Be(DealState.Open);
    }

    [Fact]
    public async Task Settlement_needs_both_parties_to_confirm()
    {
        var (lender, seeker, deal) = await OpenDealAsync();

        await lender.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/progress", new { note = (string?)null });

        var (_, afterLender) = await lender.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/settle", null);
        afterLender!.Deal.DealState.Should().Be(DealState.Progress,
            "one confirmation is not a settlement");
        afterLender.Deal.MySettlementConfirmed.Should().BeTrue();

        var (_, afterSeeker) = await seeker.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/settle", null);
        afterSeeker!.Deal.DealState.Should().Be(DealState.Completed);
    }

    [Fact]
    public async Task A_party_cannot_confirm_settlement_twice()
    {
        var (lender, _, deal) = await OpenDealAsync();

        await lender.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/progress", new { note = (string?)null });
        await lender.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/settle", null);

        var status = await lender.PostStatusAsync($"/api/deals/{deal.Id}/settle", null);

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Settlement_cannot_be_confirmed_on_a_deal_that_has_not_started_moving()
    {
        var (lender, _, deal) = await OpenDealAsync();

        var status = await lender.PostStatusAsync($"/api/deals/{deal.Id}/settle", null);

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Whoever_halts_a_deal_is_recorded_at_fault_for_it()
    {
        var (lender, seeker, deal) = await OpenDealAsync();

        var (status, halted) = await seeker.PostAsync<DealDetailDto>(
            $"/api/deals/{deal.Id}/halt", new { reason = "Supplier went quiet." });

        status.Should().Be(HttpStatusCode.OK);
        halted!.Deal.DealState.Should().Be(DealState.Halted);
        halted.Deal.IHaltedThisDeal.Should().BeTrue();

        // The v1 rule: it counts against the halting party's own profile.
        var (_, seekerProfile) = await seeker.GetAsync<ProfileSummaryDto>("/api/profiles/me");
        seekerProfile!.Trust.HaltsAtFault.Should().Be(1);

        var (_, lenderProfile) = await lender.GetAsync<ProfileSummaryDto>("/api/profiles/me");
        lenderProfile!.Trust.HaltsAtFault.Should().Be(0);
    }

    [Fact]
    public async Task A_halted_deal_cannot_be_pushed_back_into_progress_unilaterally()
    {
        var (lender, _, deal) = await OpenDealAsync();
        await lender.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/halt", new { reason = (string?)null });

        var status = await lender.PostStatusAsync(
            $"/api/deals/{deal.Id}/progress", new { note = (string?)null });

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Resuming_a_halted_deal_takes_both_parties()
    {
        var (lender, seeker, deal) = await OpenDealAsync();
        await lender.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/halt", new { reason = (string?)null });

        var (_, request) = await lender.PostAsync<ResumeRequestDto>(
            $"/api/deals/{deal.Id}/resume-request", null);

        // A one-sided request changes nothing.
        var (_, stillHalted) = await lender.GetAsync<DealDetailDto>($"/api/deals/{deal.Id}");
        stillHalted!.Deal.DealState.Should().Be(DealState.Halted);

        // And the requester cannot wave it through themselves.
        var selfAccept = await lender.PostStatusAsync(
            $"/api/deals/{deal.Id}/resume-request/{request!.Id}/respond", new { accept = true });
        selfAccept.Should().Be(HttpStatusCode.Forbidden);

        var (_, resumed) = await seeker.PostAsync<DealDetailDto>(
            $"/api/deals/{deal.Id}/resume-request/{request.Id}/respond", new { accept = true });
        resumed!.Deal.DealState.Should().Be(DealState.Progress);
    }

    [Fact]
    public async Task A_rating_is_one_per_party_per_deal_and_only_once_the_deal_has_closed()
    {
        var (lender, seeker, deal) = await OpenDealAsync();

        var tooEarly = await lender.PostStatusAsync(
            $"/api/deals/{deal.Id}/rating", new { stars = 5, comment = (string?)null });
        tooEarly.Should().Be(HttpStatusCode.BadRequest);

        await lender.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/progress", new { note = (string?)null });
        await lender.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/settle", null);
        await seeker.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/settle", null);

        var first = await lender.PostStatusAsync(
            $"/api/deals/{deal.Id}/rating", new { stars = 5, comment = "Paid early." });
        first.Should().Be(HttpStatusCode.OK);

        var second = await lender.PostStatusAsync(
            $"/api/deals/{deal.Id}/rating", new { stars = 4, comment = (string?)null });
        second.Should().Be(HttpStatusCode.Conflict, "the unique index backs this up too");

        var outOfRange = await seeker.PostStatusAsync(
            $"/api/deals/{deal.Id}/rating", new { stars = 6, comment = (string?)null });
        outOfRange.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task There_is_no_cap_on_simultaneous_deals_with_different_counterparties()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Busy Lender", ApiClient.ValidGstin());

        for (var i = 0; i < 3; i++)
        {
            var seeker = await ApiClient.RegisterAsync(
                _factory, ProfileRole.Seeker, $"Seeker {i}", ApiClient.ValidGstin());

            var interest = await lender.PostOkAsync<InterestDto>(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });
            await seeker.PostAsync<InterestDto>(
                $"/api/interests/{interest.Id}/respond", new { accept = true });

            var deal = await ApiClient.OpenDealAsync(lender, seeker, interest.Id, new
            {
                interestId = interest.Id,
                category = DealCategory.RawMaterial,
                categorySubTypeId = 10,
                capacity = 100 + i,
                capacityUnit = "kg",
                materialDescription = "Oranges",
                description = $"Deal {i}.",
                estimatedSettlementTime = DateTimeOffset.UtcNow.AddDays(30)
            });

            var status = HttpStatusCode.OK;

            status.Should().Be(HttpStatusCode.OK);
        }

        var (_, open) = await lender.GetAsync<List<DealRowDto>>("/api/deals/open");
        open!.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_closed_deal_moves_from_open_deals_into_history()
    {
        var (lender, _, deal) = await OpenDealAsync();
        await lender.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/halt", new { reason = (string?)null });

        var (_, open) = await lender.GetAsync<List<DealRowDto>>("/api/deals/open");
        open!.Should().NotContain(d => d.Id == deal.Id);

        var (_, history) = await lender.GetAsync<List<DealRowDto>>(
            $"/api/deals/history?state={(int)DealState.Halted}");
        history!.Should().Contain(d => d.Id == deal.Id);
    }

    [Fact]
    public async Task A_deal_belonging_to_two_other_parties_is_not_readable()
    {
        var (_, _, deal) = await OpenDealAsync();
        var stranger = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Stranger", ApiClient.ValidGstin());

        var (status, _) = await stranger.GetAsync<DealDetailDto>($"/api/deals/{deal.Id}");

        status.Should().Be(HttpStatusCode.Forbidden);
    }
}
