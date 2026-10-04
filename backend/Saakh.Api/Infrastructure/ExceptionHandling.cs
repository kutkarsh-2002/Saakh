using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Saakh.Api.Services;

namespace Saakh.Api.Infrastructure;

/// <summary>
/// Turns a <see cref="DomainException"/> into its stated status code and plain-language
/// message, so business-rule wording lives next to the rule rather than in a controller.
/// Everything else becomes a generic 500 with the detail logged, never returned.
/// </summary>
public class DomainExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DomainExceptionHandler> _log;
    private readonly IHostEnvironment _env;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> log, IHostEnvironment env)
    {
        _log = log;
        _env = env;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken ct)
    {
        ApiError payload;

        switch (exception)
        {
            case DomainException domain:
                context.Response.StatusCode = domain.StatusCode;
                payload = new ApiError(domain.Message);
                _log.LogInformation("Rule rejected {Path}: {Message}", context.Request.Path, domain.Message);
                break;

            case UnauthorizedAccessException:
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                payload = new ApiError("You do not have access to that.");
                break;

            default:
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                _log.LogError(exception, "Unhandled error on {Path}", context.Request.Path);
                payload = new ApiError(
                    _env.IsDevelopment()
                        ? exception.Message
                        : "Something went wrong on our side. Try again in a moment.");
                break;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions), ct);
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public static class RateLimitPolicies
{
    /// <summary>
    /// The GSTIN endpoint hits the real government registry and spends a provider credit on
    /// every call, so it is the tightest limit in the app (tech-stack.md security notes).
    /// </summary>
    public const string Gstin = "gstin";

    public const string Signup = "signup";
}
