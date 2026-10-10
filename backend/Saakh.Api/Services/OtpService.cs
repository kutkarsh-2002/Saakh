using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Saakh.Api.Configuration;

namespace Saakh.Api.Services;

/// <summary>
/// The outcome of asking for a code. <paramref name="Sent"/> is false when the
/// caller is still inside the resend window, in which case the existing code
/// stands and <paramref name="RetryAfterSeconds"/> says how long is left.
/// </summary>
public record OtpIssueResult(bool Sent, string? Code, int RetryAfterSeconds, int ExpiresInSeconds);

/// <summary>
/// Phone OTP is mandatory at signup on both verification paths (spec §5). No SMS gateway is
/// in the free stack, so the code is issued in-process and logged; the signup flow and its
/// gating are real, only the delivery channel is stubbed.
/// </summary>
public interface IOtpService
{
    /// <summary>Issues a code for the phone number, or reports the wait if one was just sent.</summary>
    Task<OtpIssueResult> RequestAsync(OtpRecipient to, CancellationToken ct = default);

    /// <summary>Where a code would be delivered, in the words the signup form shows.</summary>
    string Destination { get; }

    Task<bool> VerifyAsync(string phone, string code, CancellationToken ct = default);

    /// <summary>True once <see cref="VerifyAsync"/> has succeeded for this number.</summary>
    bool IsVerified(string phone);

    void Consume(string phone);
}

public class InMemoryOtpService : IOtpService
{
    private record Pending(string Code, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, int Attempts);

    // Instance state, not static. The service is registered as a singleton, so this
    // behaves identically at runtime — and it means one caller's codes cannot leak
    // into another's, which static fields did across every test in the process.
    private readonly ConcurrentDictionary<string, Pending> Issued = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> Verified = new();

    private const int MaxAttempts = 5;

    private readonly ILogger<InMemoryOtpService> _log;
    private readonly IOtpChannel _channel;
    private readonly bool _revealCode;
    private readonly TimeSpan _lifetime;
    private readonly TimeSpan _cooldown;

    public InMemoryOtpService(ILogger<InMemoryOtpService> log, IHostEnvironment env,
        IOtpChannel channel, IOptions<OtpOptions> options)
    {
        _log = log;
        _channel = channel;

        var otp = options.Value;

        // The code is only ever handed back when nothing is actually delivering it.
        // A configured gateway settles the question: returning the code alongside a
        // real SMS would hand anyone a way past the check the SMS exists to perform.
        var canReveal = !channel.IsRealDelivery;

        if (otp.RevealCodeInResponse == true && !canReveal)
        {
            _log.LogWarning(
                "Otp:RevealCodeInResponse is set, but {Provider} delivers for real, so the code "
                + "is not being returned. Phone verification only means something when the code "
                + "travels to the phone and nowhere else.",
                channel.GetType().Name);
        }

        // Unset means "outside Production only", which keeps a fresh clone demoable.
        _revealCode = canReveal && (otp.RevealCodeInResponse ?? !env.IsProduction());

        _lifetime = TimeSpan.FromMinutes(Math.Max(1, otp.LifetimeMinutes));
        _cooldown = TimeSpan.FromSeconds(Math.Max(0, otp.ResendCooldownSeconds));
    }

    public string Destination => _channel.Destination;

    public async Task<OtpIssueResult> RequestAsync(OtpRecipient to, CancellationToken ct = default)
    {
        var key = Normalize(to.Phone);
        var now = DateTimeOffset.UtcNow;

        // A resend window, enforced here rather than only in the UI: without it this
        // form is a button that sends messages to a phone number of the caller's
        // choosing, as fast as they can click.
        if (Issued.TryGetValue(key, out var existing) && existing.ExpiresAt > now)
        {
            var wait = existing.IssuedAt + _cooldown - now;

            if (wait > TimeSpan.Zero)
            {
                return new OtpIssueResult(
                    false,
                    _revealCode ? existing.Code : null,
                    (int)Math.Ceiling(wait.TotalSeconds),
                    (int)Math.Ceiling((existing.ExpiresAt - now).TotalSeconds));
            }
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        var delivery = await _channel.SendOtpAsync(to with { Phone = key }, code, ct);

        if (!delivery.Sent)
        {
            // Nothing was delivered, so nothing is recorded as issued. Storing a code
            // the user cannot receive would leave them staring at a form that can only
            // reject them.
            throw new DomainException(
                delivery.Error ?? "We could not send the code. Try again in a moment.");
        }

        Issued[key] = new Pending(code, now, now.Add(_lifetime), 0);

        if (!_channel.IsRealDelivery)
        {
            _log.LogInformation("OTP for {Phone}: {Code}", key, code);
        }

        return new OtpIssueResult(
            true,
            _revealCode ? code : null,
            (int)_cooldown.TotalSeconds,
            (int)_lifetime.TotalSeconds);
    }

    public Task<bool> VerifyAsync(string phone, string code, CancellationToken ct = default)
    {
        var key = Normalize(phone);

        if (!Issued.TryGetValue(key, out var pending))
        {
            return Task.FromResult(false);
        }

        if (pending.ExpiresAt < DateTimeOffset.UtcNow || pending.Attempts >= MaxAttempts)
        {
            Issued.TryRemove(key, out _);
            return Task.FromResult(false);
        }

        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(pending.Code),
                System.Text.Encoding.UTF8.GetBytes(code.Trim())))
        {
            Issued[key] = pending with { Attempts = pending.Attempts + 1 };
            return Task.FromResult(false);
        }

        Issued.TryRemove(key, out _);
        Verified[key] = DateTimeOffset.UtcNow.Add(_lifetime);
        return Task.FromResult(true);
    }

    public bool IsVerified(string phone)
    {
        var key = Normalize(phone);
        return Verified.TryGetValue(key, out var until) && until > DateTimeOffset.UtcNow;
    }

    public void Consume(string phone) => Verified.TryRemove(Normalize(phone), out _);

    private static string Normalize(string phone) =>
        new(phone.Where(char.IsDigit).ToArray());
}
