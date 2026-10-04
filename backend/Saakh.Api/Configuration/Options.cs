namespace Saakh.Api.Configuration;

public class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "saakh-api";
    public string Audience { get; set; } = "saakh-web";

    /// <summary>Signing key. Supplied via configuration/environment, never committed.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Access tokens stay short-lived (tech-stack.md security notes).</summary>
    public int AccessTokenMinutes { get; set; } = 30;

    public int RefreshTokenDays { get; set; } = 14;
}

public class GstinOptions
{
    public const string Section = "Gstin";

    /// <summary>"Mock" (default outside Production) or "GstinApi" for the live gstinapi.in provider.</summary>
    public string Provider { get; set; } = "Mock";

    public string BaseUrl { get; set; } = "https://api.gstinapi.in";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}

public class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Matches the Docker volume mount point from tech-stack.md's compose file.</summary>
    public string EvidenceRoot { get; set; } = "storage/evidence";

    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    public string[] AllowedContentTypes { get; set; } =
        ["image/jpeg", "image/png", "image/webp", "application/pdf"];
}

public class EmailOptions
{
    public const string Section = "Email";

    /// <summary>"Log" (development default), "Smtp" (Mailtrap), or "SendGrid".</summary>
    public string Provider { get; set; } = "Log";

    public string FromAddress { get; set; } = "no-reply@saakh.app";
    public string FromName { get; set; } = "The Saakh Team";

    public string SendGridApiKey { get; set; } = string.Empty;

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 2525;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
}

/// <summary>
/// Request limits on the two endpoints worth protecting. Configurable because
/// the right numbers differ per deployment: the GSTIN limit exists to protect a
/// metered third-party quota, and the signup limit to blunt automated abuse.
/// </summary>
public class RateLimitOptions
{
    public const string Section = "RateLimits";

    /// <summary>Live GSTIN lookups spend a provider credit, so this stays tight.</summary>
    public int GstinPermitLimit { get; set; } = 10;

    public int GstinWindowMinutes { get; set; } = 10;

    public int SignupPermitLimit { get; set; } = 30;

    public int SignupWindowMinutes { get; set; } = 5;
}

/// <summary>
/// How this instance treats the schema it depends on. Migrations are a
/// deliberate deployment step, not something that should quietly happen on boot
/// in an environment running more than one replica — two instances migrating the
/// same database at once is how you lose a deployment.
/// </summary>
public class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>
    /// Apply pending migrations during startup. Unset means "only in
    /// Development", which keeps a fresh clone runnable in one command while
    /// leaving a real deployment to the <c>migrate</c> bundle. Compose sets it
    /// explicitly so the demo stack comes up from cold with no second step.
    /// </summary>
    public bool? MigrateOnStartup { get; set; }
}
