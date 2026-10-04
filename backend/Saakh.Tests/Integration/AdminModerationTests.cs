using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;
using Saakh.Api.Services;
using Xunit;

namespace Saakh.Tests.Integration;

/// <summary>
/// The Admin console: the human checkpoint for accounts the platform cannot
/// verify automatically, plus the moderation actions. Every decision here is
/// supposed to be logged with an actor, which is what makes it auditable.
/// </summary>
[Collection(ApiCollection.Name)]
public class AdminModerationTests
{
    private readonly SaakhApiFactory _factory;

    public AdminModerationTests(SaakhApiFactory factory) => _factory = factory;

    /// <summary>
    /// Admins are created by an operator, not by signup, so the test creates one
    /// the same way the seeder does and then signs in through the real endpoint.
    /// </summary>
    private async Task<ApiClient> AdminAsync()
    {
        var email = $"admin-{Guid.NewGuid():N}@saakh.test";

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var db = scope.ServiceProvider.GetRequiredService<SaakhDbContext>();

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = "Test Admin",
                PhoneVerified = true
            };

            (await users.CreateAsync(user, "Test@Password1")).Succeeded.Should().BeTrue();
            await users.AddToRoleAsync(user, SaakhRoles.Admin);

            db.Admins.Add(new Admin { Id = Guid.NewGuid(), UserId = user.Id, Name = "Test Admin" });
            await db.SaveChangesAsync();
        }

        var client = new ApiClient(_factory.CreateClient());
        var (status, auth) = await client.PostAsync<AuthResultDto>(
            "/api/auth/login", new { email, password = "Test@Password1" });

        status.Should().Be(HttpStatusCode.OK);
        client.Authenticate(auth!.AccessToken);
        client.Session = auth.Session;
        return client;
    }

    /// <summary>Puts an account into Pending by submitting evidence for it directly.</summary>
    private async Task<ApiClient> PendingAccountAsync(string name)
    {
        var account = await ApiClient.RegisterAsync(_factory, ProfileRole.Seeker, name);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SaakhDbContext>();

        var profile = await db.Profiles.SingleAsync(p => p.Id == account.ProfileId);
        profile.VerificationStatus = VerificationStatus.Pending;

        db.EvidenceDocuments.Add(new EvidenceDocument
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            StorageKey = $"test/{Guid.NewGuid():N}.pdf",
            FileName = "shop-licence.pdf",
            ContentType = "application/pdf",
            SizeBytes = 2048,
            DocumentType = "Shop licence",
            Decision = EvidenceDecision.AwaitingReview
        });

        await db.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task An_admin_has_no_trading_profile_and_no_discovery_surface()
    {
        var admin = await AdminAsync();

        admin.Session!.Profile.Should().BeNull();
        admin.Session.Admin.Should().NotBeNull();

        var (status, _) = await admin.GetAsync<object>("/api/opportunities");
        status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_trader_cannot_reach_the_admin_console()
    {
        var trader = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Nosy Lender", ApiClient.ValidGstin());

        var (status, _) = await trader.GetAsync<object>("/api/admin/verification-queue");

        status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Approving_an_account_unlocks_every_tab_for_it()
    {
        var admin = await AdminAsync();
        var pending = await PendingAccountAsync("Awaiting Review");

        var locked = await pending.GetAsync<object>("/api/opportunities");
        locked.Status.Should().Be(HttpStatusCode.Forbidden);

        var (status, approved) = await admin.PostAsync<ProfileSummaryDto>(
            $"/api/admin/profiles/{pending.ProfileId}/verification",
            new { approve = true, rejectionReason = (string?)null });

        status.Should().Be(HttpStatusCode.OK);
        approved!.VerificationStatus.Should().Be(VerificationStatus.Active);

        // A fresh token, because the old one still carries the stale claim.
        var reauth = await SignInAgainAsync(pending);
        var (unlocked, _) = await reauth.GetAsync<PagedResultDto<OpportunityRowDto>>("/api/opportunities");
        unlocked.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_rejection_must_carry_a_reason_and_that_reason_reaches_the_user()
    {
        var admin = await AdminAsync();
        var pending = await PendingAccountAsync("To Be Rejected");

        var noReason = await admin.PostStatusAsync(
            $"/api/admin/profiles/{pending.ProfileId}/verification",
            new { approve = false, rejectionReason = (string?)null });
        noReason.Should().Be(HttpStatusCode.BadRequest,
            "the reason is the only guidance the vendor gets");

        var (status, rejected) = await admin.PostAsync<ProfileSummaryDto>(
            $"/api/admin/profiles/{pending.ProfileId}/verification",
            new { approve = false, rejectionReason = "The licence photo is too dark to read." });

        status.Should().Be(HttpStatusCode.OK);
        rejected!.VerificationStatus.Should().Be(VerificationStatus.Rejected);
        rejected.RejectionReason.Should().Contain("too dark");

        var (_, ownProfile) = await pending.GetAsync<ProfileSummaryDto>("/api/profiles/me");
        ownProfile!.RejectionReason.Should().Contain("too dark");
    }

    [Fact]
    public async Task An_account_that_submitted_nothing_cannot_be_decided_on()
    {
        var admin = await AdminAsync();
        var untouched = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Nothing Submitted");

        var status = await admin.PostStatusAsync(
            $"/api/admin/profiles/{untouched.ProfileId}/verification",
            new { approve = true, rejectionReason = (string?)null });

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Suspension_needs_a_window_and_removes_the_profile_from_search()
    {
        var admin = await AdminAsync();
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Watching Lender", ApiClient.ValidGstin());
        var target = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "To Be Suspended", ApiClient.ValidGstin());

        var noWindow = await admin.PostStatusAsync(
            $"/api/admin/profiles/{target.ProfileId}/moderate",
            new { actionType = AdminActionType.Suspend, suspensionDuration = (int?)null, notes = "No window." });
        noWindow.Should().Be(HttpStatusCode.BadRequest);

        var (status, suspended) = await admin.PostAsync<ProfileSummaryDto>(
            $"/api/admin/profiles/{target.ProfileId}/moderate",
            new
            {
                actionType = AdminActionType.Suspend,
                suspensionDuration = SuspensionDuration.OneWeek,
                notes = "Repeated unexplained halts."
            });

        status.Should().Be(HttpStatusCode.OK);
        suspended!.AvailabilityStatus.Should().Be(AvailabilityStatus.Suspended);
        suspended.SuspensionEndDate.Should().NotBeNull("a one-week suspension is time-bound");

        var (_, page) = await lender.GetAsync<PagedResultDto<OpportunityRowDto>>(
            "/api/opportunities?pageSize=100");
        page!.Items.Should().NotContain(r => r.Profile.Id == target.ProfileId);

        var interest = await lender.PostStatusAsync(
            "/api/interests", new { toProfileId = target.ProfileId, note = (string?)null });
        interest.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_permanent_suspension_carries_no_end_date()
    {
        var admin = await AdminAsync();
        var target = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Permanently Out", ApiClient.ValidGstin());

        var (_, suspended) = await admin.PostAsync<ProfileSummaryDto>(
            $"/api/admin/profiles/{target.ProfileId}/moderate",
            new
            {
                actionType = AdminActionType.Suspend,
                suspensionDuration = SuspensionDuration.Permanent,
                notes = "Permanent."
            });

        suspended!.AvailabilityStatus.Should().Be(AvailabilityStatus.Suspended);
        suspended.SuspensionEndDate.Should().BeNull();
    }

    [Fact]
    public async Task Lifting_a_suspension_restores_the_profile()
    {
        var admin = await AdminAsync();
        var target = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Coming Back", ApiClient.ValidGstin());

        await admin.PostAsync<ProfileSummaryDto>(
            $"/api/admin/profiles/{target.ProfileId}/moderate",
            new
            {
                actionType = AdminActionType.Suspend,
                suspensionDuration = SuspensionDuration.OneWeek,
                notes = "Temporary."
            });

        var (_, lifted) = await admin.PostAsync<ProfileSummaryDto>(
            $"/api/admin/profiles/{target.ProfileId}/moderate",
            new { actionType = AdminActionType.SuspensionLifted, suspensionDuration = (int?)null, notes = "Resolved." });

        lifted!.AvailabilityStatus.Should().Be(AvailabilityStatus.Active);
        lifted.SuspensionEndDate.Should().BeNull();
    }

    [Fact]
    public async Task A_suspended_party_freezes_the_deal_for_both_sides()
    {
        var admin = await AdminAsync();
        var lender = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Lender, "Frozen Lender", ApiClient.ValidGstin());
        var seeker = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Frozen Seeker", ApiClient.ValidGstin());

        var interest = await lender.PostOkAsync<InterestDto>(
            "/api/interests", new { toProfileId = seeker.ProfileId, note = (string?)null });
        await seeker.PostAsync<InterestDto>($"/api/interests/{interest.Id}/respond", new { accept = true });

        var (_, deal) = await lender.PostAsync<DealRowDto>("/api/deals/tickets", new
        {
            interestId = interest.Id,
            category = DealCategory.RawMaterial,
            categorySubTypeId = 10,
            capacity = 300,
            capacityUnit = "kg",
            materialDescription = "Oranges",
            description = "Will be frozen.",
            estimatedSettlementTime = DateTimeOffset.UtcNow.AddDays(30)
        });

        await admin.PostAsync<ProfileSummaryDto>(
            $"/api/admin/profiles/{seeker.ProfileId}/moderate",
            new
            {
                actionType = AdminActionType.Suspend,
                suspensionDuration = SuspensionDuration.OneMonth,
                notes = "Under investigation."
            });

        // The lender did nothing wrong and is still blocked — stated in the spec
        // and surfaced in the Admin console before the action is taken.
        var blocked = await lender.PostStatusAsync(
            $"/api/deals/{deal!.Id}/progress", new { note = (string?)null });
        blocked.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Every_moderation_action_is_logged_against_the_admin_who_took_it()
    {
        var admin = await AdminAsync();
        var target = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Logged Target", ApiClient.ValidGstin());

        await admin.PostAsync<ProfileSummaryDto>(
            $"/api/admin/profiles/{target.ProfileId}/moderate",
            new { actionType = AdminActionType.Warning, suspensionDuration = (int?)null, notes = "First warning." });

        var (status, log) = await admin.GetAsync<List<AdminActionLogDto>>(
            $"/api/admin/action-log?profileId={target.ProfileId}");

        status.Should().Be(HttpStatusCode.OK);
        var entry = log.Should().ContainSingle().Subject;

        entry.ActionType.Should().Be(AdminActionType.Warning);
        entry.AdminName.Should().Be("Test Admin");
        entry.Notes.Should().Be("First warning.");
        entry.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_warning_changes_nothing_about_access()
    {
        var admin = await AdminAsync();
        var target = await ApiClient.RegisterAsync(
            _factory, ProfileRole.Seeker, "Warned Only", ApiClient.ValidGstin());

        var (_, warned) = await admin.PostAsync<ProfileSummaryDto>(
            $"/api/admin/profiles/{target.ProfileId}/moderate",
            new { actionType = AdminActionType.Warning, suspensionDuration = (int?)null, notes = "Be careful." });

        warned!.AvailabilityStatus.Should().Be(AvailabilityStatus.Active);

        var (status, _) = await target.GetAsync<PagedResultDto<OpportunityRowDto>>("/api/opportunities");
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_verification_queue_puts_accounts_awaiting_review_first()
    {
        var admin = await AdminAsync();
        await PendingAccountAsync("Queue Entry");

        var (status, queue) = await admin.GetAsync<List<VerificationQueueRowDto>>(
            "/api/admin/verification-queue");

        status.Should().Be(HttpStatusCode.OK);
        queue.Should().NotBeNullOrEmpty();
        queue![0].Profile.VerificationStatus.Should().Be(VerificationStatus.Pending,
            "those are the ones with something to decide on");

        var pendingRow = queue.First(r => r.Profile.VerificationStatus == VerificationStatus.Pending);
        pendingRow.Evidence.Should().NotBeEmpty();
        pendingRow.HoursWaiting.Should().NotBeNull();
    }

    private async Task<ApiClient> SignInAgainAsync(ApiClient account)
    {
        var email = account.Session!.Email;
        var client = new ApiClient(_factory.CreateClient());

        var (_, auth) = await client.PostAsync<AuthResultDto>(
            "/api/auth/login", new { email, password = "Test@Password1" });

        client.Authenticate(auth!.AccessToken);
        client.Session = auth.Session;
        return client;
    }
}
