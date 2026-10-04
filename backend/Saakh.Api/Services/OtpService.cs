using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Saakh.Api.Services;

/// <summary>
/// Phone OTP is mandatory at signup on both verification paths (spec §5). No SMS gateway is
/// in the free stack, so the code is issued in-process and logged; the signup flow and its
/// gating are real, only the delivery channel is stubbed.
/// </summary>
public interface IOtpService
{
    /// <summary>Issues a code for the phone number and returns it when the dev channel is active.</summary>
    Task<string?> RequestAsync(string phone, CancellationToken ct = default);

    Task<bool> VerifyAsync(string phone, string code, CancellationToken ct = default);

    /// <summary>True once <see cref="VerifyAsync"/> has succeeded for this number.</summary>
    bool IsVerified(string phone);

    void Consume(string phone);
}

public class InMemoryOtpService : IOtpService
{
    private record Pending(string Code, DateTimeOffset ExpiresAt, int Attempts);

    private static readonly ConcurrentDictionary<string, Pending> Issued = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> Verified = new();

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const int MaxAttempts = 5;

    private readonly ILogger<InMemoryOtpService> _log;
    private readonly bool _revealCode;

    public InMemoryOtpService(ILogger<InMemoryOtpService> log, IHostEnvironment env)
    {
        _log = log;
        // Outside Production the code comes back in the response so the flow is demoable
        // without an SMS gateway. In Production it is only ever logged.
        _revealCode = !env.IsProduction();
    }

    public Task<string?> RequestAsync(string phone, CancellationToken ct = default)
    {
        var key = Normalize(phone);
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        Issued[key] = new Pending(code, DateTimeOffset.UtcNow.Add(Lifetime), 0);
        _log.LogInformation("OTP for {Phone}: {Code}", key, code);

        return Task.FromResult(_revealCode ? code : null);
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
        Verified[key] = DateTimeOffset.UtcNow.Add(Lifetime);
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
