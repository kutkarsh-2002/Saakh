using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Saakh.Api.Configuration;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Hubs;
using Saakh.Api.Jobs;
using Saakh.Api.Services;
using Xunit;

namespace Saakh.Tests.Integration;

/// <summary>
/// The platform closing a deal on its own is the most consequential thing it does
/// without being asked: it ends a live arrangement and writes a finding onto two trust
/// records. These pin down when it fires, when it holds off, and that it never
/// fabricates a rating to do it.
/// </summary>
[Collection(ApiCollection.Name)]
public class SettlementOverdueTests
{
    private readonly SaakhApiFactory _factory;

    public SettlementOverdueTests(SaakhApiFactory factory) => _factory = factory;

    private async Task<(ApiClient Lender, ApiClient Seeker, DealRowDto Deal)> OpenDealAsync()
    {
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Overdue Lender", ApiClient.ValidGstin());
        var seeker = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Overdue Seeker", ApiClient.ValidGstin());

        var interest = await lender.PostOkAsync<InterestDto>(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });
        await seeker.PostAsync<InterestDto>($"/api/interests/{interest.Id}/respond", new { accept = true });

        var deal = await ApiClient.OpenDealAsync(lender, seeker, interest.Id);
        return (lender, seeker, deal);
    }

    /// <summary>
    /// Moves the agreed date into the past. A deal cannot be created overdue — the
    /// service refuses it — so the only way to reach this state is for time to pass.
    /// </summary>
    private async Task SetSettlementAsync(Guid dealId, DateTimeOffset when) =>
        await _factory.WithDbAsync(async db =>
        {
            var deal = await db.Deals.FirstAsync(d => d.Id == dealId);
            deal.EstimatedSettlementTime = when;
            await db.SaveChangesAsync();
        });

    private async Task RunJobAsync(int graceDays = 7, bool autoClose = true)
    {
        using var scope = _factory.Services.CreateScope();

        var job = new SettlementOverdueJob(
            scope.ServiceProvider.GetRequiredService<SaakhDbContext>(),
            scope.ServiceProvider.GetRequiredService<INotificationService>(),
            scope.ServiceProvider.GetRequiredService<IRealtimePublisher>(),
            Options.Create(new SettlementOptions { GraceDays = graceDays, AutoCloseOverdue = autoClose }),
            NullLogger<SettlementOverdueJob>.Instance);

        await job.RunAsync();
    }

    [Fact]
    public async Task A_deal_past_its_date_is_warned_but_not_closed_during_the_grace_period()
    {
        var (lender, _, deal) = await OpenDealAsync();

        await SetSettlementAsync(deal.Id, DateTimeOffset.UtcNow.AddDays(-2));
        await RunJobAsync();

        var detail = await lender.GetOkAsync<DealDetailDto>($"/api/deals/{deal.Id}");

        // Settlement slipping by a couple of days is ordinary trade.
        detail.Deal.DealState.Should().Be(DealState.Open);

        var notifications = await lender.GetOkAsync<List<NotificationDto>>("/api/notifications");
        notifications.Should().Contain(n => n.Kind == NotificationKinds.SettlementOverdue);
    }

    [Fact]
    public async Task The_warning_is_sent_once_however_often_the_sweep_runs()
    {
        var (lender, _, deal) = await OpenDealAsync();

        await SetSettlementAsync(deal.Id, DateTimeOffset.UtcNow.AddDays(-2));

        await RunJobAsync();
        await RunJobAsync();
        await RunJobAsync();

        var notifications = await lender.GetOkAsync<List<NotificationDto>>("/api/notifications");

        notifications.Count(n => n.Kind == NotificationKinds.SettlementOverdue)
            .Should().Be(1, "an hourly job must not become an hourly nag");
    }

    [Fact]
    public async Task Once_the_grace_period_runs_out_the_platform_closes_the_deal()
    {
        var (lender, _, deal) = await OpenDealAsync();

        await SetSettlementAsync(deal.Id, DateTimeOffset.UtcNow.AddDays(-10));
        await RunJobAsync();

        var detail = await lender.GetOkAsync<DealDetailDto>($"/api/deals/{deal.Id}");

        detail.Deal.DealState.Should().Be(DealState.Halted);
        detail.Deal.ClosedAt.Should().NotBeNull();

        // Nobody triggered it, so nobody is recorded as having halted it.
        detail.Deal.HaltedByProfileId.Should().BeNull();
        detail.Deal.IHaltedThisDeal.Should().BeFalse();

        detail.Timeline.Should().Contain(e => e.Note != null && e.Note.Contains("Closed by the platform"));
    }

    [Fact]
    public async Task The_closure_lands_on_both_trust_records()
    {
        var (lender, seeker, deal) = await OpenDealAsync();

        await SetSettlementAsync(deal.Id, DateTimeOffset.UtcNow.AddDays(-10));
        await RunJobAsync();

        var lenderProfile = await lender.GetOkAsync<ProfileSummaryDto>("/api/profiles/me");
        var seekerProfile = await seeker.GetOkAsync<ProfileSummaryDto>("/api/profiles/me");

        lenderProfile.Trust.DealsClosedOverdue.Should().Be(1);
        seekerProfile.Trust.DealsClosedOverdue.Should().Be(1);

        // It is recorded as its own finding, not disguised as a counterparty's verdict.
        lenderProfile.Trust.RatingsReceived.Should().Be(0);
        lenderProfile.Trust.AverageStars.Should().BeNull();
        lenderProfile.Trust.HaltsAtFault.Should().Be(0, "no party triggered this");
    }

    [Fact]
    public async Task A_settled_deal_is_left_alone_however_late_it_was()
    {
        var (lender, seeker, deal) = await OpenDealAsync();

        await lender.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/progress", new { note = (string?)null });
        await lender.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/settle", new { });
        await seeker.PostAsync<DealDetailDto>($"/api/deals/{deal.Id}/settle", new { });

        await SetSettlementAsync(deal.Id, DateTimeOffset.UtcNow.AddDays(-30));
        await RunJobAsync();

        var detail = await lender.GetOkAsync<DealDetailDto>($"/api/deals/{deal.Id}");

        detail.Deal.DealState.Should().Be(DealState.Completed);

        var profile = await lender.GetOkAsync<ProfileSummaryDto>("/api/profiles/me");
        profile.Trust.DealsClosedOverdue.Should().Be(0, "it settled, whenever it settled");
    }

    [Fact]
    public async Task A_deal_still_within_its_date_is_untouched()
    {
        var (lender, _, deal) = await OpenDealAsync();

        await RunJobAsync();

        var detail = await lender.GetOkAsync<DealDetailDto>($"/api/deals/{deal.Id}");
        detail.Deal.DealState.Should().Be(DealState.Open);

        var notifications = await lender.GetOkAsync<List<NotificationDto>>("/api/notifications");
        notifications.Should().NotContain(n => n.Kind == NotificationKinds.SettlementOverdue);
    }

    [Fact]
    public async Task Auto_closing_can_be_switched_off_without_losing_the_warning()
    {
        var (lender, _, deal) = await OpenDealAsync();

        await SetSettlementAsync(deal.Id, DateTimeOffset.UtcNow.AddDays(-30));
        await RunJobAsync(autoClose: false);

        var detail = await lender.GetOkAsync<DealDetailDto>($"/api/deals/{deal.Id}");

        detail.Deal.DealState.Should().Be(DealState.Open, "closing is opt-out, warning is not");

        var notifications = await lender.GetOkAsync<List<NotificationDto>>("/api/notifications");
        notifications.Should().Contain(n => n.Kind == NotificationKinds.SettlementOverdue);
    }
}
