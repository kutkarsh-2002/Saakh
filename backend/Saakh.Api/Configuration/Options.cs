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

/// <summary>
/// Phone verification. There is no SMS gateway in the free stack, so the code is
/// issued in-process and written to the log; the flow and its gating are real,
/// only the delivery channel is stubbed.
/// </summary>
public class OtpOptions
{
    public const string Section = "Otp";

    /// <summary>
    /// Return the code in the API response so the signup flow can be completed
    /// without an SMS gateway. Unset means "outside Production only".
    ///
    /// Turning this on defeats phone verification — anyone can confirm any number
    /// — so it belongs in a demo stack and nowhere else. The compose file enables
    /// it deliberately, and says so.
    /// </summary>
    public bool? RevealCodeInResponse { get; set; }

    /// <summary>How long a code stays valid.</summary>
    public int LifetimeMinutes { get; set; } = 10;

    /// <summary>
    /// How long before another code can be requested for the same number. Stops a
    /// signup form being used to pump messages at somebody else's phone.
    /// </summary>
    public int ResendCooldownSeconds { get; set; } = 60;
}

/// <summary>
/// The SMS gateway that carries the signup code.
///
/// "Log" is the default and sends nothing: the code goes to the API log and back to
/// the signup form, which makes the flow demoable and makes phone verification
/// meaningless. Configure a real provider and the code goes to the handset instead,
/// and stops being returned.
/// </summary>
public class SmsOptions
{
    public const string Section = "Sms";

    /// <summary>"Log" (default), "Fast2Sms", "Msg91" or "Twilio".</summary>
    public string Provider { get; set; } = "Log";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Fast2SMS only. "q" (Quick SMS) is the route for senders without DLT
    /// registration and is the default. "otp" uses Fast2SMS's OTP route, which their
    /// dashboard gates behind website verification.
    /// </summary>
    public string Fast2SmsRoute { get; set; } = "q";

    /// <summary>MSG91 only: the DLT-approved template the code is sent through.</summary>
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>Twilio only.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Twilio only: the number messages are sent from, in E.164.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Prefixed to a number given without one. India.</summary>
    public string DefaultCountryCode { get; set; } = "91";

    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// What the platform does when an agreed settlement date passes.
///
/// The date is the one term both parties commit to that the platform later acts on
/// by itself, so the grace period matters: settlement slipping by a day is ordinary
/// trade, and closing on the stroke of the deadline would punish normal business.
/// </summary>
public class SettlementOptions
{
    public const string Section = "Settlement";

    /// <summary>Days past the agreed date before the platform closes the deal.</summary>
    public int GraceDays { get; set; } = 7;

    /// <summary>
    /// Off by default outside the demo stack. Closing deals and marking both trust
    /// records is irreversible, so it is switched on deliberately.
    /// </summary>
    public bool AutoCloseOverdue { get; set; } = true;
}

public class JobsOptions
{
    public const string Section = "Jobs";

    /// <summary>
    /// Run the Hangfire server and its recurring sweeps in this instance. On by
    /// default, which is what the compose stack and a normal deployment want.
    ///
    /// Turn it off where the API scales to zero or the database is metered by
    /// time-online rather than by query. Hangfire polls its storage on a fixed
    /// interval whether or not there is work, so an idle instance still keeps the
    /// database permanently awake — on Azure SQL's free serverless offer that
    /// polling alone consumes the whole monthly vCore-second allowance in a
    /// couple of days and the database is then paused for the rest of the month.
    /// With this off, nothing is enqueued and the sweeps below never run, so the
    /// overdue-settlement closure and the GSTIN retry have to be triggered by
    /// hand or by an external scheduler.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the Hangfire server asks storage for queued work. Only read when
    /// <see cref="Enabled"/> is set. Every poll is a query, so this is the dial
    /// that decides how much a mostly-idle deployment costs.
    /// </summary>
    public int QueuePollIntervalSeconds { get; set; } = 15;
}
