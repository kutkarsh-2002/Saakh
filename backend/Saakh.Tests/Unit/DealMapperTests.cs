using FluentAssertions;
using Saakh.Api.Domain;
using Saakh.Api.Mapping;
using Saakh.Api.Services;
using Xunit;

namespace Saakh.Tests.Unit;

/// <summary>
/// A deal row is rendered from one party's point of view, and several fields
/// invert depending on who is asking. The halt-fault attribution in particular
/// is the v1 rule that counts against a profile's reputation, so it is tested
/// from both sides of the same deal.
/// </summary>
public class DealMapperTests
{
    private static readonly Guid LenderId = Guid.NewGuid();
    private static readonly Guid SeekerId = Guid.NewGuid();

    private static Profile Party(Guid id, ProfileRole role, string name) => new()
    {
        Id = id,
        Role = role,
        Name = name,
        Country = "India",
        State = "Maharashtra",
        District = "Pune",
        Category = DealCategory.RawMaterial,
        CapacityUnit = "kg",
        VerificationStatus = VerificationStatus.Active,
        AvailabilityStatus = AvailabilityStatus.Active
    };

    private static Deal SampleDeal(DealState state = DealState.Progress)
    {
        var lender = Party(LenderId, ProfileRole.Lender, "Deshmukh Traders");
        var seeker = Party(SeekerId, ProfileRole.Seeker, "Patil Kirana Store");

        return new Deal
        {
            Id = Guid.NewGuid(),
            Reference = "SK-10001",
            LenderProfileId = LenderId,
            LenderProfile = lender,
            SeekerProfileId = SeekerId,
            SeekerProfile = seeker,
            Category = DealCategory.RawMaterial,
            Capacity = 500,
            CapacityUnit = "kg",
            Description = "Weekly supply on 30-day credit.",
            EstimatedSettlementTime = DateTimeOffset.UtcNow.AddDays(30),
            State = state,
            Country = "India",
            LocationState = "Maharashtra",
            District = "Pune"
        };
    }

    [Fact]
    public void Each_party_sees_itself_on_its_own_side_of_the_deal()
    {
        var deal = SampleDeal();

        deal.ToRow(LenderId, TrustStatsService.Empty).MySide.Should().Be(ProfileRole.Lender);
        deal.ToRow(SeekerId, TrustStatsService.Empty).MySide.Should().Be(ProfileRole.Seeker);
    }

    [Fact]
    public void Each_party_sees_the_other_one_as_the_counterparty()
    {
        var deal = SampleDeal();

        deal.ToRow(LenderId, TrustStatsService.Empty).Counterparty.Name.Should().Be("Patil Kirana Store");
        deal.ToRow(SeekerId, TrustStatsService.Empty).Counterparty.Name.Should().Be("Deshmukh Traders");
    }

    [Fact]
    public void The_party_who_triggered_the_halt_is_the_one_flagged_for_it()
    {
        var deal = SampleDeal(DealState.Halted);
        deal.HaltedByProfileId = SeekerId;
        deal.HaltedAt = DateTimeOffset.UtcNow;

        // The v1 rule: whoever clicks halt carries it, and both parties are shown
        // the same attribution rather than a vague "this deal was halted".
        deal.ToRow(SeekerId, TrustStatsService.Empty).IHaltedThisDeal.Should().BeTrue();
        deal.ToRow(LenderId, TrustStatsService.Empty).IHaltedThisDeal.Should().BeFalse();
    }

    [Fact]
    public void Settlement_confirmation_is_reported_from_the_asking_party_s_perspective()
    {
        var deal = SampleDeal();
        deal.LenderConfirmedSettlement = true;
        deal.SeekerConfirmedSettlement = false;

        var asLender = deal.ToRow(LenderId, TrustStatsService.Empty);
        asLender.MySettlementConfirmed.Should().BeTrue();
        asLender.TheirSettlementConfirmed.Should().BeFalse();

        var asSeeker = deal.ToRow(SeekerId, TrustStatsService.Empty);
        asSeeker.MySettlementConfirmed.Should().BeFalse();
        asSeeker.TheirSettlementConfirmed.Should().BeTrue();
    }

    [Theory]
    [InlineData(DealState.Open, false)]
    [InlineData(DealState.Progress, false)]
    [InlineData(DealState.Completed, true)]
    [InlineData(DealState.Halted, true)]
    public void Rating_only_opens_once_the_deal_has_closed(DealState state, bool canRate)
    {
        var deal = SampleDeal(state);

        deal.ToRow(LenderId, TrustStatsService.Empty).CanRate.Should().Be(canRate);
    }

    [Fact]
    public void A_party_who_has_already_rated_cannot_rate_again()
    {
        var deal = SampleDeal(DealState.Completed);
        deal.Ratings.Add(new Rating
        {
            Id = Guid.NewGuid(),
            DealId = deal.Id,
            RaterProfileId = LenderId,
            RatedProfileId = SeekerId,
            Stars = 5
        });

        deal.ToRow(LenderId, TrustStatsService.Empty).CanRate.Should().BeFalse();
        deal.ToRow(SeekerId, TrustStatsService.Empty).CanRate.Should().BeTrue(
            "the other party has still not had their turn");
    }

    [Fact]
    public void A_deal_is_frozen_when_either_side_is_restricted_by_an_admin()
    {
        var deal = SampleDeal();
        deal.SeekerProfile.AvailabilityStatus = AvailabilityStatus.Suspended;

        // Both parties see it frozen, including the one who did nothing wrong.
        deal.ToRow(LenderId, TrustStatsService.Empty).Frozen.Should().BeTrue();
        deal.ToRow(SeekerId, TrustStatsService.Empty).Frozen.Should().BeTrue();
    }

    [Fact]
    public void Only_a_still_pending_resume_request_is_surfaced()
    {
        var deal = SampleDeal(DealState.Halted);
        deal.ResumeRequests.Add(new ResumeRequest
        {
            Id = Guid.NewGuid(),
            DealId = deal.Id,
            RequestedByProfileId = LenderId,
            Status = ResumeRequestStatus.Declined,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        });

        deal.ToRow(SeekerId, TrustStatsService.Empty).PendingResumeRequest.Should().BeNull();

        deal.ResumeRequests.Add(new ResumeRequest
        {
            Id = Guid.NewGuid(),
            DealId = deal.Id,
            RequestedByProfileId = LenderId,
            Status = ResumeRequestStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var row = deal.ToRow(SeekerId, TrustStatsService.Empty);
        row.PendingResumeRequest.Should().NotBeNull();
        row.PendingResumeRequest!.RequestedByMe.Should().BeFalse(
            "the seeker is being asked, not asking");
    }
}
