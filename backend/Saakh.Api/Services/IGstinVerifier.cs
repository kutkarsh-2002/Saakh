using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Saakh.Api.Configuration;

namespace Saakh.Api.Services;

/// <param name="IsValid">True only when the registry confirmed an active registration.</param>
/// <param name="LegalName">Legal/trade name the registry returned, when it returned one.</param>
/// <param name="Status">Registry status string, e.g. "Active", "Cancelled".</param>
/// <param name="Message">Human-readable reason, surfaced to the signup form on failure.</param>
/// <param name="TransientFailure">
/// True when the lookup could not be completed (timeout, provider 5xx) as opposed to the
/// GSTIN being genuinely invalid. Drives the Hangfire retry job rather than a hard rejection.
/// </param>
public record GstinVerificationResult(
    bool IsValid,
    string? LegalName,
    string? Status,
    string? Message,
    bool TransientFailure = false)
{
    public static GstinVerificationResult Invalid(string message) => new(false, null, null, message);

    public static GstinVerificationResult Transient(string message) =>
        new(false, null, null, message, TransientFailure: true);
}

/// <summary>
/// Real-time GSTIN verification against the government registry (spec §5). The interface is
/// what protects the build from a third-party free-tier term change (tech-stack.md).
/// </summary>
public interface IGstinVerifier
{
    Task<GstinVerificationResult> VerifyAsync(string gstin, CancellationToken ct = default);
}

public static class GstinFormat
{
    /// <summary>
    /// The checksum/format pattern the provider's own SDKs validate locally first, so a
    /// mistyped number fails instantly without spending a credit (tech-stack.md).
    /// The same regex is mirrored client-side in the Angular signup form.
    /// </summary>
    public const string Pattern = @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$";

    private static readonly Regex Compiled = new(Pattern, RegexOptions.Compiled);

    public static bool IsWellFormed(string? gstin) =>
        !string.IsNullOrWhiteSpace(gstin) && Compiled.IsMatch(gstin.Trim().ToUpperInvariant());

    public static string Normalize(string gstin) => gstin.Trim().ToUpperInvariant();
}

/// <summary>
/// Default verifier outside Production, and the one used by automated runs so tests never
/// burn the monthly free quota (tech-stack.md). Deterministic: format decides the outcome,
/// with two reserved prefixes to rehearse the failure paths.
/// </summary>
public class MockGstinVerifier : IGstinVerifier
{
    private readonly ILogger<MockGstinVerifier> _log;

    public MockGstinVerifier(ILogger<MockGstinVerifier> log) => _log = log;

    public Task<GstinVerificationResult> VerifyAsync(string gstin, CancellationToken ct = default)
    {
        var normalized = GstinFormat.Normalize(gstin);
        _log.LogInformation("MockGstinVerifier: verifying {Gstin}", normalized);

        if (!GstinFormat.IsWellFormed(normalized))
        {
            return Task.FromResult(GstinVerificationResult.Invalid(
                "That GSTIN is not in the correct format. Check the 15 characters and try again."));
        }

        // Reserved rehearsal prefixes, so the rejection and retry paths can be demoed
        // without a live registry call.
        if (normalized.StartsWith("00"))
        {
            return Task.FromResult(GstinVerificationResult.Invalid(
                "This GSTIN is not registered on the government registry."));
        }

        if (normalized.StartsWith("99"))
        {
            return Task.FromResult(GstinVerificationResult.Transient(
                "The government registry did not respond. We will retry shortly."));
        }

        var stateCode = normalized[..2];
        var legalName = $"Registered Taxpayer ({stateCode}{normalized[2..7]})";
        return Task.FromResult(new GstinVerificationResult(true, legalName, "Active", "GSTIN verified."));
    }
}

/// <summary>
/// Live gstinapi.in implementation. Every call hits the real government network and spends a
/// credit, so the format check runs first and only well-formed numbers reach the provider.
/// </summary>
public class GstinApiVerifier : IGstinVerifier
{
    private readonly HttpClient _http;
    private readonly GstinOptions _options;
    private readonly ILogger<GstinApiVerifier> _log;

    public GstinApiVerifier(HttpClient http, IOptions<GstinOptions> options, ILogger<GstinApiVerifier> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    public async Task<GstinVerificationResult> VerifyAsync(string gstin, CancellationToken ct = default)
    {
        var normalized = GstinFormat.Normalize(gstin);

        if (!GstinFormat.IsWellFormed(normalized))
        {
            return GstinVerificationResult.Invalid(
                "That GSTIN is not in the correct format. Check the 15 characters and try again.");
        }

        try
        {
            using var response = await _http.GetAsync($"/v1/gstin/{normalized}", ct);

            if ((int)response.StatusCode >= 500)
            {
                _log.LogWarning("GSTIN provider returned {Status} for {Gstin}", response.StatusCode, normalized);
                return GstinVerificationResult.Transient(
                    "The government registry did not respond. We will retry shortly.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return GstinVerificationResult.Invalid(
                    "This GSTIN is not registered on the government registry.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("GSTIN lookup failed {Status}: {Detail}", response.StatusCode, detail);
                return GstinVerificationResult.Invalid("We could not verify this GSTIN. Please check and retry.");
            }

            var payload = await response.Content.ReadFromJsonAsync<GstinApiResponse>(cancellationToken: ct);

            if (payload is null || !payload.Success)
            {
                return GstinVerificationResult.Invalid(
                    payload?.Message ?? "This GSTIN could not be verified on the government registry.");
            }

            var active = string.Equals(payload.Data?.Status, "Active", StringComparison.OrdinalIgnoreCase);

            return active
                ? new GstinVerificationResult(true, payload.Data?.LegalName ?? payload.Data?.TradeName,
                    payload.Data?.Status, "GSTIN verified.")
                : GstinVerificationResult.Invalid(
                    $"This GSTIN is registered but its status is \"{payload.Data?.Status}\". " +
                    "Only an active registration can be used to open an account.");
        }
        catch (TaskCanceledException)
        {
            return GstinVerificationResult.Transient(
                "The government registry timed out. We will retry shortly.");
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "GSTIN lookup transport failure for {Gstin}", normalized);
            return GstinVerificationResult.Transient(
                "We could not reach the government registry. We will retry shortly.");
        }
    }

    private class GstinApiResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("data")] public GstinApiData? Data { get; set; }
    }

    private class GstinApiData
    {
        [JsonPropertyName("gstin")] public string? Gstin { get; set; }
        [JsonPropertyName("legal_name")] public string? LegalName { get; set; }
        [JsonPropertyName("trade_name")] public string? TradeName { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
    }
}
