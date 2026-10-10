using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Saakh.Api.Configuration;
using Saakh.Api.Services;
using Xunit;

namespace Saakh.Tests.Unit;

/// <summary>
/// Phone verification is mandatory on both signup paths, so its edges matter:
/// a resend window the UI cannot be trusted to enforce, an expiry that really
/// expires, and a demo channel that must stay off unless it is asked for.
/// </summary>
public class OtpServiceTests
{
    private static InMemoryOtpService Service(OtpOptions? options = null,
        string environment = "Development", IOtpChannel? channel = null)
        => new(
            NullLogger<InMemoryOtpService>.Instance,
            new StubEnvironment(environment),
            channel ?? new LoggingOtpChannel(NullLogger<LoggingOtpChannel>.Instance),
            Options.Create(options ?? new OtpOptions()));

    private static string Phone() => $"9{Random.Shared.NextInt64(100000000, 999999999)}";

    private static OtpRecipient Recipient() => new(Phone(), "vendor@example.com");

    [Fact]
    public async Task A_second_request_inside_the_window_does_not_issue_a_new_code()
    {
        var service = Service();
        var phone = Phone();

        var first = await service.RequestAsync(new OtpRecipient(phone, null));
        var second = await service.RequestAsync(new OtpRecipient(phone, null));

        first.Sent.Should().BeTrue();
        second.Sent.Should().BeFalse("the form must not be a button that pumps messages at a number");
        second.RetryAfterSeconds.Should().BeGreaterThan(0);
        // The code already sent still stands, so a user who clicks twice is not stranded.
        second.Code.Should().Be(first.Code);
    }

    [Fact]
    public async Task The_cooldown_is_per_number_not_global()
    {
        var service = Service();

        var a = await service.RequestAsync(Recipient());
        var b = await service.RequestAsync(Recipient());

        a.Sent.Should().BeTrue();
        b.Sent.Should().BeTrue("one person signing up cannot block another");
    }

    [Fact]
    public async Task A_cooldown_of_zero_lets_a_code_be_reissued_immediately()
    {
        var service = Service(new OtpOptions { ResendCooldownSeconds = 0 });
        var phone = Phone();

        var first = await service.RequestAsync(new OtpRecipient(phone, null));
        var second = await service.RequestAsync(new OtpRecipient(phone, null));

        second.Sent.Should().BeTrue();
        second.Code.Should().NotBe(first.Code, "a resend issues a fresh code");
    }

    [Fact]
    public async Task The_code_is_withheld_in_production_unless_it_is_explicitly_asked_for()
    {
        var hidden = await Service(environment: "Production").RequestAsync(Recipient());

        hidden.Code.Should().BeNull("revealing it defeats the check this step exists for");

        var revealed = await Service(
            new OtpOptions { RevealCodeInResponse = true }, "Production").RequestAsync(Recipient());

        revealed.Code.Should().NotBeNull("a demo stack opts in deliberately");
    }

    [Fact]
    public async Task A_real_gateway_stops_the_code_coming_back_even_if_asked_for()
    {
        // The setting cannot be used to hand out codes that are also being texted:
        // that would be a way straight past the check the SMS exists to perform.
        var service = Service(
            new OtpOptions { RevealCodeInResponse = true },
            "Development",
            new StubOtpChannel(realDelivery: true));

        var issued = await service.RequestAsync(Recipient());

        issued.Sent.Should().BeTrue();
        issued.Code.Should().BeNull();
    }

    [Fact]
    public async Task A_real_gateway_is_handed_the_code_for_the_number_that_asked()
    {
        var sms = new StubOtpChannel(realDelivery: true);
        var service = Service(channel: sms);
        var phone = Phone();

        await service.RequestAsync(new OtpRecipient(phone, null));

        sms.Sent.Should().ContainSingle();
        sms.Sent[0].Phone.Should().Be(phone);
        sms.Sent[0].Code.Should().MatchRegex("^[0-9]{6}$");

        // And that code is the one the user can verify with.
        (await service.VerifyAsync(phone, sms.Sent[0].Code)).Should().BeTrue();
    }

    [Fact]
    public async Task A_gateway_failure_issues_no_code_at_all()
    {
        var service = Service(channel: new StubOtpChannel(realDelivery: true, fails: true));
        var phone = Phone();

        var send = async () => await service.RequestAsync(new OtpRecipient(phone, null));

        // Storing a code nobody can receive leaves the user at a form that can only
        // reject them, so the failure is surfaced instead.
        await send.Should().ThrowAsync<DomainException>();
        (await service.VerifyAsync(phone, "123456")).Should().BeFalse();
    }

    [Fact]
    public async Task The_email_channel_sends_the_code_to_the_signup_address()
    {
        var email = new RecordingEmailSender();
        var channel = new EmailOtpChannel(
            email,
            Options.Create(new EmailOptions { Provider = "Smtp" }),
            NullLogger<EmailOtpChannel>.Instance);

        var service = Service(channel: channel);
        var phone = Phone();

        var issued = await service.RequestAsync(new OtpRecipient(phone, "vendor@example.com"));

        email.Sent.Should().ContainSingle();
        email.Sent[0].To.Should().Be("vendor@example.com");

        // The code is in the body and nowhere else: a real channel never hands it back.
        issued.Code.Should().BeNull();
        var code = System.Text.RegularExpressions.Regex.Match(email.Sent[0].Body, @"\d{6}").Value;
        code.Should().NotBeEmpty();
        (await service.VerifyAsync(phone, code)).Should().BeTrue();
    }

    [Fact]
    public async Task The_email_channel_refuses_when_no_address_was_given()
    {
        var channel = new EmailOtpChannel(
            new RecordingEmailSender(),
            Options.Create(new EmailOptions { Provider = "Smtp" }),
            NullLogger<EmailOtpChannel>.Instance);

        var send = async () => await Service(channel: channel).RequestAsync(new OtpRecipient(Phone(), null));

        await send.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task The_email_channel_still_reveals_the_code_while_email_itself_is_stubbed()
    {
        // Email:Provider=Log writes to the API log, so nothing reaches an inbox and a
        // fresh clone would be unable to finish signup unless the code came back.
        var channel = new EmailOtpChannel(
            new RecordingEmailSender(),
            Options.Create(new EmailOptions { Provider = "Log" }),
            NullLogger<EmailOtpChannel>.Instance);

        var issued = await Service(channel: channel)
            .RequestAsync(new OtpRecipient(Phone(), "vendor@example.com"));

        issued.Code.Should().NotBeNull();
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string toAddress, string toName, string subject, string textBody,
            CancellationToken ct = default)
        {
            Sent.Add((toAddress, subject, textBody));
            return Task.CompletedTask;
        }
    }

    private sealed class StubOtpChannel : IOtpChannel
    {
        private readonly bool _fails;

        public StubOtpChannel(bool realDelivery, bool fails = false)
        {
            IsRealDelivery = realDelivery;
            _fails = fails;
        }

        public bool IsRealDelivery { get; }

        public List<(string Phone, string Code)> Sent { get; } = [];

        public string Destination => "your phone";

        public Task<OtpDeliveryResult> SendOtpAsync(OtpRecipient to, string code,
            CancellationToken ct = default)
        {
            if (_fails)
            {
                return Task.FromResult(new OtpDeliveryResult(false, "The SMS gateway rejected the message."));
            }

            Sent.Add((to.Phone, code));
            return Task.FromResult(OtpDeliveryResult.Ok);
        }
    }

    [Fact]
    public async Task A_wrong_code_is_refused_and_the_right_one_is_accepted()
    {
        var service = Service();
        var phone = Phone();
        var issued = await service.RequestAsync(new OtpRecipient(phone, null));

        (await service.VerifyAsync(phone, "000000")).Should().BeFalse();
        (await service.VerifyAsync(phone, issued.Code!)).Should().BeTrue();
        service.IsVerified(phone).Should().BeTrue();
    }

    [Fact]
    public async Task A_code_cannot_be_used_twice()
    {
        var service = Service();
        var phone = Phone();
        var issued = await service.RequestAsync(new OtpRecipient(phone, null));

        await service.VerifyAsync(phone, issued.Code!);

        (await service.VerifyAsync(phone, issued.Code!)).Should().BeFalse();
    }

    [Fact]
    public async Task Guessing_is_cut_off_after_five_attempts()
    {
        var service = Service();
        var phone = Phone();
        var issued = await service.RequestAsync(new OtpRecipient(phone, null));

        for (var i = 0; i < 5; i++)
        {
            (await service.VerifyAsync(phone, "000000")).Should().BeFalse();
        }

        // Six digits is 10^6; without this, the right answer is reachable by script.
        (await service.VerifyAsync(phone, issued.Code!)).Should().BeFalse("the code is burned");
    }

    [Fact]
    public async Task The_number_is_matched_however_it_was_typed()
    {
        var service = Service();
        var phone = Phone();
        var issued = await service.RequestAsync(new OtpRecipient(phone, null));

        var spaced = $"{phone[..5]} {phone[5..]}";

        (await service.VerifyAsync(spaced, issued.Code!)).Should().BeTrue();
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public StubEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Saakh.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
