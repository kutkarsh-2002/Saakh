using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Xunit;

namespace Saakh.Tests.Integration;

/// <summary>
/// A deal only exists once both parties have agreed its terms.
///
/// The reason is the settlement date. The platform acts on that date by itself — it
/// closes the deal and records the outcome on both trust records — so a date only one
/// side ever chose must not be able to bind the other.
/// </summary>
[Collection(ApiCollection.Name)]
public class DealProposalTests
{
    private readonly SaakhApiFactory _factory;

    public DealProposalTests(SaakhApiFactory factory) => _factory = factory;

    private async Task<(ApiClient Lender, ApiClient Seeker, Guid InterestId)> AcceptedInterestAsync()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Proposing Lender", ApiClient.ValidGstin());
        var seeker = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Deciding Seeker", ApiClient.ValidGstin());

        var interest = await lender.PostOkAsync<InterestDto>(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });

        await seeker.PostAsync<InterestDto>($"/api/interests/{interest.Id}/respond", new { accept = true });

        return (lender, seeker, interest.Id);
    }

    private static object Terms(Guid interestId, int days = 30, decimal capacity = 500) => new
    {
        interestId,
        category = DealCategory.RawMaterial,
        categorySubTypeId = 10,
        capacity,
        capacityUnit = "kg",
        materialDescription = "Nagpur oranges, grade A",
        description = "Weekly supply on credit.",
        estimatedSettlementTime = DateTimeOffset.UtcNow.AddDays(days)
    };

    [Fact]
    public async Task Proposing_terms_does_not_create_a_deal()
    {
        var (lender, seeker, interestId) = await AcceptedInterestAsync();

        var proposal = await lender.PostOkAsync<DealProposalDto>(
            "/api/deals/proposals", Terms(interestId));

        proposal.Status.Should().Be(ProposalStatus.Pending);
        proposal.DealId.Should().BeNull();

        // Neither side has a deal yet, which is the whole point.
        var (_, lenderDeals) = await lender.GetAsync<List<DealRowDto>>("/api/deals/open");
        var (_, seekerDeals) = await seeker.GetAsync<List<DealRowDto>>("/api/deals/open");

        lenderDeals!.Should().BeEmpty();
        seekerDeals!.Should().BeEmpty();
    }

    [Fact]
    public async Task The_deal_opens_only_when_the_other_party_agrees()
    {
        var (lender, seeker, interestId) = await AcceptedInterestAsync();

        var proposal = await lender.PostOkAsync<DealProposalDto>(
            "/api/deals/proposals", Terms(interestId));

        var deal = await seeker.PostOkAsync<DealRowDto>(
            $"/api/deals/proposals/{proposal.Id}/accept", new { });

        deal.DealState.Should().Be(DealState.Open);

        var (_, seekerDeals) = await seeker.GetAsync<List<DealRowDto>>("/api/deals/open");
        seekerDeals!.Should().ContainSingle().Which.Id.Should().Be(deal.Id);
    }

    [Fact]
    public async Task A_party_cannot_agree_its_own_terms()
    {
        var (lender, _, interestId) = await AcceptedInterestAsync();

        var proposal = await lender.PostOkAsync<DealProposalDto>(
            "/api/deals/proposals", Terms(interestId));

        // Otherwise "agreed by both parties" would mean nothing at all.
        var status = await lender.PostStatusAsync($"/api/deals/proposals/{proposal.Id}/accept", new { });

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Countering_replaces_the_terms_and_passes_the_turn_back()
    {
        var (lender, seeker, interestId) = await AcceptedInterestAsync();

        var first = await lender.PostOkAsync<DealProposalDto>(
            "/api/deals/proposals", Terms(interestId, days: 15));

        var counter = await seeker.PostOkAsync<DealProposalDto>(
            $"/api/deals/proposals/{first.Id}/counter", Terms(interestId, days: 45));

        counter.ProposedByMe.Should().BeTrue("the seeker wrote the amended terms");
        counter.Status.Should().Be(ProposalStatus.Pending);

        var thread = await lender.GetOkAsync<ProposalThreadDto>($"/api/deals/proposals/{interestId}");

        thread.Live!.Id.Should().Be(counter.Id, "the counter is what is now on the table");
        thread.Live.AwaitingMyResponse.Should().BeTrue("the turn passed back to the lender");
        thread.History.Should().HaveCount(2);
        thread.History[0].Status.Should().Be(ProposalStatus.Countered);
    }

    [Fact]
    public async Task Superseded_terms_can_no_longer_be_agreed()
    {
        var (lender, seeker, interestId) = await AcceptedInterestAsync();

        var first = await lender.PostOkAsync<DealProposalDto>(
            "/api/deals/proposals", Terms(interestId, days: 15));

        await seeker.PostOkAsync<DealProposalDto>(
            $"/api/deals/proposals/{first.Id}/counter", Terms(interestId, days: 45));

        // The lender must not be able to reach back and accept the date the seeker
        // already rejected.
        var status = await seeker.PostStatusAsync($"/api/deals/proposals/{first.Id}/accept", new { });

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_one_set_of_terms_is_live_at_a_time()
    {
        var (lender, _, interestId) = await AcceptedInterestAsync();

        await lender.PostOkAsync<DealProposalDto>("/api/deals/proposals", Terms(interestId));

        // Two live sets is how two parties each end up believing a different thing was agreed.
        var status = await lender.PostStatusAsync("/api/deals/proposals", Terms(interestId, days: 60));

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Withdrawing_clears_the_way_for_fresh_terms()
    {
        var (lender, _, interestId) = await AcceptedInterestAsync();

        var first = await lender.PostOkAsync<DealProposalDto>("/api/deals/proposals", Terms(interestId));

        await lender.PostOkAsync<DealProposalDto>($"/api/deals/proposals/{first.Id}/withdraw", new { });

        var second = await lender.PostOkAsync<DealProposalDto>(
            "/api/deals/proposals", Terms(interestId, days: 60));

        second.Status.Should().Be(ProposalStatus.Pending);
    }

    [Fact]
    public async Task Only_the_proposer_can_withdraw_their_terms()
    {
        var (lender, seeker, interestId) = await AcceptedInterestAsync();

        var proposal = await lender.PostOkAsync<DealProposalDto>(
            "/api/deals/proposals", Terms(interestId));

        var status = await seeker.PostStatusAsync(
            $"/api/deals/proposals/{proposal.Id}/withdraw", new { });

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_settlement_date_in_the_past_cannot_be_proposed()
    {
        var (lender, _, interestId) = await AcceptedInterestAsync();

        // Otherwise the platform would close the deal and mark both records the moment
        // it was created.
        var status = await lender.PostStatusAsync("/api/deals/proposals", Terms(interestId, days: -1));

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_outsider_cannot_see_or_answer_the_terms()
    {
        var (lender, _, interestId) = await AcceptedInterestAsync();

        var proposal = await lender.PostOkAsync<DealProposalDto>(
            "/api/deals/proposals", Terms(interestId));

        var outsider = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Nosy Outsider", ApiClient.ValidGstin());

        (await outsider.PostStatusAsync($"/api/deals/proposals/{proposal.Id}/accept", new { }))
            .Should().Be(HttpStatusCode.Forbidden);

        var (readStatus, _) = await outsider.GetAsync<ProposalThreadDto>(
            $"/api/deals/proposals/{interestId}");

        readStatus.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Agreed_terms_carry_into_the_deal_unchanged()
    {
        var (lender, seeker, interestId) = await AcceptedInterestAsync();

        var proposal = await lender.PostOkAsync<DealProposalDto>(
            "/api/deals/proposals", Terms(interestId, days: 21, capacity: 1234));

        var deal = await seeker.PostOkAsync<DealRowDto>(
            $"/api/deals/proposals/{proposal.Id}/accept", new { });

        // What was agreed is what binds: the date in particular, since the platform
        // enforces it later.
        deal.Capacity.Should().Be(1234);
        deal.EstimatedSettlementTime.Should().BeCloseTo(proposal.EstimatedSettlementTime, TimeSpan.FromSeconds(1));
        deal.Description.Should().Be(proposal.Description);
    }

    [Fact]
    public async Task A_conversation_that_already_produced_a_deal_takes_no_more_terms()
    {
        var (lender, seeker, interestId) = await AcceptedInterestAsync();

        await ApiClient.OpenDealAsync(lender, seeker, interestId);

        var status = await lender.PostStatusAsync("/api/deals/proposals", Terms(interestId, days: 60));

        status.Should().Be(HttpStatusCode.BadRequest);
    }
}
