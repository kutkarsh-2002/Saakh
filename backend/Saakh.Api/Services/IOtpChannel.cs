using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Saakh.Api.Configuration;

namespace Saakh.Api.Services;

/// <summary>
/// Who the code is going to. A channel takes what it needs: the SMS gateways use the
/// phone, the email channel uses the address. Both travel together so the OTP service
/// does not have to know which channel is configured.
/// </summary>
public record OtpRecipient(string Phone, string? Email);

/// <summary>The outcome of handing a message to a gateway, so a failure can be said out loud.</summary>
public record OtpDeliveryResult(bool Sent, string? Error = null)
{
    public static readonly OtpDeliveryResult Ok = new(true);
}

public interface IOtpChannel
{
    /// <summary>True when this channel actually delivers the code somewhere.</summary>
    bool IsRealDelivery { get; }

    /// <summary>Where the code went, in the words the signup form shows the user.</summary>
    string Destination { get; }

    Task<OtpDeliveryResult> SendOtpAsync(OtpRecipient to, string code, CancellationToken ct = default);
}

/// <summary>
/// The default. Writes the code to the log and nothing else, so a test run or a local
/// demo never spends credits or messages a real person.
///
/// It reports <see cref="IsRealDelivery"/> false, which is what makes the API hand the
/// code back to the signup form. That is a stub standing in for verification, not
/// verification: with this provider selected, anyone can confirm any number.
/// </summary>
public class LoggingOtpChannel : IOtpChannel
{
    private readonly ILogger<LoggingOtpChannel> _log;

    public LoggingOtpChannel(ILogger<LoggingOtpChannel> log) => _log = log;

    public bool IsRealDelivery => false;

    public string Destination => "the server log";

    public Task<OtpDeliveryResult> SendOtpAsync(OtpRecipient to, string code, CancellationToken ct = default)
    {
        _log.LogInformation("OTP (not delivered, Log channel) for {Phone}: your Saakh code is {Code}",
            to.Phone, code);
        return Task.FromResult(OtpDeliveryResult.Ok);
    }
}

/// <summary>
/// Fast2SMS. Two routes, chosen by <see cref="SmsOptions.Fast2SmsRoute"/>:
///
/// <list type="bullet">
/// <item><c>q</c> (Quick SMS, the default) is the route for senders without DLT
/// registration. It carries a message of our own wording, which suits this service
/// because the code, its expiry and its verification all live here.</item>
/// <item><c>otp</c> is Fast2SMS's OTP route. It refuses with "complete website
/// verification" until that step is done in their dashboard.</item>
/// </list>
/// </summary>
public class Fast2SmsChannel : IOtpChannel
{
    private readonly HttpClient _http;
    private readonly SmsOptions _options;
    private readonly ILogger<Fast2SmsChannel> _log;

    public Fast2SmsChannel(HttpClient http, IOptions<SmsOptions> options, ILogger<Fast2SmsChannel> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    public bool IsRealDelivery => true;

    public string Destination => "your phone";

    public async Task<OtpDeliveryResult> SendOtpAsync(OtpRecipient to, string code, CancellationToken ct = default)
    {
        // Both routes take a bare 10-digit Indian number, with no country code.
        var national = NationalNumber(to.Phone);

        var route = string.IsNullOrWhiteSpace(_options.Fast2SmsRoute)
            ? "q"
            : _options.Fast2SmsRoute.Trim().ToLowerInvariant();

        // The OTP route substitutes the code into Fast2SMS's own template; Quick SMS
        // carries the whole message, so the wording is ours and says what it is for.
        var url = route == "otp"
            ? "https://www.fast2sms.com/dev/bulkV2"
              + $"?route=otp&variables_values={Uri.EscapeDataString(code)}"
              + $"&numbers={Uri.EscapeDataString(national)}"
              + "&flash=0"
            : "https://www.fast2sms.com/dev/bulkV2"
              + "?route=q&language=english&flash=0"
              + $"&message={Uri.EscapeDataString($"{code} is your Saakh verification code. It expires in 10 minutes. Do not share it with anyone.")}"
              + $"&numbers={Uri.EscapeDataString(national)}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("authorization", _options.ApiKey);

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Fast2SMS refused the message for {Phone}: {Status} {Body}",
                    national, (int)response.StatusCode, body);
                return new OtpDeliveryResult(false, DescribeFailure(body));
            }

            return OtpDeliveryResult.Ok;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Fast2SMS was unreachable for {Phone}", national);
            return new OtpDeliveryResult(false, "The SMS gateway could not be reached.");
        }
    }

    /// <summary>Surfaces the gateway's own wording where it gives one; it is usually actionable.</summary>
    private static string DescribeFailure(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);

            if (json.RootElement.TryGetProperty("message", out var message))
            {
                return message.ValueKind == JsonValueKind.Array
                    ? string.Join(" ", message.EnumerateArray().Select(e => e.GetString()))
                    : message.GetString() ?? "The SMS gateway rejected the message.";
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall through to the generic wording rather than echoing HTML.
        }

        return "The SMS gateway rejected the message.";
    }

    /// <summary>Last ten digits, so 9876543210, 919876543210 and +91 98765 43210 all agree.</summary>
    internal static string NationalNumber(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits;
    }
}

/// <summary>
/// MSG91's flow API. Needs a DLT-approved template whose variable carries the code,
/// which is what Indian regulation requires of a sender using its own identity.
/// </summary>
public class Msg91Channel : IOtpChannel
{
    private readonly HttpClient _http;
    private readonly SmsOptions _options;
    private readonly ILogger<Msg91Channel> _log;

    public Msg91Channel(HttpClient http, IOptions<SmsOptions> options, ILogger<Msg91Channel> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    public bool IsRealDelivery => true;

    public string Destination => "your phone";

    public async Task<OtpDeliveryResult> SendOtpAsync(OtpRecipient to, string code, CancellationToken ct = default)
    {
        var payload = new
        {
            template_id = _options.TemplateId,
            short_url = "0",
            recipients = new[]
            {
                new Dictionary<string, string>
                {
                    ["mobiles"] = $"{_options.DefaultCountryCode}{Fast2SmsChannel.NationalNumber(to.Phone)}",
                    ["otp"] = code,
                    ["var1"] = code
                }
            }
        };

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "https://control.msg91.com/api/v5/flow/")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.TryAddWithoutValidation("authkey", _options.ApiKey);

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("MSG91 refused the message: {Status} {Body}",
                    (int)response.StatusCode, body);
                return new OtpDeliveryResult(false, "The SMS gateway rejected the message.");
            }

            return OtpDeliveryResult.Ok;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "MSG91 was unreachable");
            return new OtpDeliveryResult(false, "The SMS gateway could not be reached.");
        }
    }
}

/// <summary>
/// Twilio, for numbers outside India. Called over plain HTTP rather than through the
/// SDK, which keeps one more dependency out of the shipping build.
/// </summary>
public class TwilioChannel : IOtpChannel
{
    private readonly HttpClient _http;
    private readonly SmsOptions _options;
    private readonly ILogger<TwilioChannel> _log;

    public TwilioChannel(HttpClient http, IOptions<SmsOptions> options, ILogger<TwilioChannel> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    public bool IsRealDelivery => true;

    public string Destination => "your phone";

    public async Task<OtpDeliveryResult> SendOtpAsync(OtpRecipient to, string code, CancellationToken ct = default)
    {
        var digits = new string(to.Phone.Where(char.IsDigit).ToArray());
        var e164 = digits.Length > 10 ? $"+{digits}" : $"+{_options.DefaultCountryCode}{digits}";

        var form = new Dictionary<string, string>
        {
            ["To"] = e164,
            ["From"] = _options.FromNumber,
            ["Body"] = $"{code} is your Saakh verification code. It expires in 10 minutes."
        };

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Messages.json")
            {
                Content = new FormUrlEncodedContent(form)
            };

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.ApiKey}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("Twilio refused the message: {Status} {Body}",
                    (int)response.StatusCode, body);
                return new OtpDeliveryResult(false, "The SMS gateway rejected the message.");
            }

            return OtpDeliveryResult.Ok;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Twilio was unreachable");
            return new OtpDeliveryResult(false, "The SMS gateway could not be reached.");
        }
    }
}

/// <summary>
/// Delivers the code to the signup email address instead of a handset.
///
/// This exists because Indian A2P SMS is DLT-regulated and every gateway gates its
/// API behind payment or verification, which puts real SMS out of reach of a build
/// that has to cost nothing. Email has genuine free tiers at this volume.
///
/// It is an honest trade, not a disguise: this verifies control of an inbox, not of
/// a handset, and the signup form says which one it checked. The flow it exercises —
/// a code issued, delivered out of band, fetched, typed back, expiring, rate-limited
/// — is the same one an SMS gateway would drive.
/// </summary>
public class EmailOtpChannel : IOtpChannel
{
    private readonly IEmailSender _email;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailOtpChannel> _log;

    public EmailOtpChannel(IEmailSender email, IOptions<EmailOptions> options,
        ILogger<EmailOtpChannel> log)
    {
        _email = email;
        _options = options.Value;
        _log = log;
    }

    /// <summary>
    /// Only real once the email provider itself is. With Email:Provider=Log the message
    /// goes to the API log, and the code is still handed back to the form — otherwise a
    /// fresh clone could not complete signup at all.
    /// </summary>
    public bool IsRealDelivery => !_options.Provider.Equals("Log", StringComparison.OrdinalIgnoreCase);

    public string Destination => "your email";

    public async Task<OtpDeliveryResult> SendOtpAsync(OtpRecipient to, string code,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(to.Email))
        {
            // The signup form collects the address a step before the phone, so this
            // means the client did not send it rather than that the user has none.
            return new OtpDeliveryResult(false,
                "We need your email address before we can send the code.");
        }

        var body =
            $"""
             {code} is your Saakh verification code.

             It expires in 10 minutes. Do not share it with anyone — Saakh will never
             ask you for it.

             If you did not start creating a Saakh account, you can ignore this message.
             """;

        try
        {
            await _email.SendAsync(to.Email, to.Email, $"{code} is your Saakh code", body, ct);
            return OtpDeliveryResult.Ok;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not email the signup code to {Address}", to.Email);
            return new OtpDeliveryResult(false,
                "We could not send the code to your email address. Check it and try again.");
        }
    }
}
