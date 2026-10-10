using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Saakh.Api.Configuration;
using Saakh.Api.Data;
using Saakh.Api.Domain;
using Saakh.Api.Hubs;
using Saakh.Api.Infrastructure;
using Saakh.Api.Jobs;
using Saakh.Api.Services;
using Saakh.Api.Validators;

var builder = WebApplication.CreateBuilder(args);

// `dotnet run --seed` loads the Bogus-generated demo data and exits (tech-stack.md). It is
// never wired into startup, so a real deployment cannot accidentally seed itself.
var seedOnly = args.Contains("--seed");
var resetOnSeed = args.Contains("--reset");

// ---- options -------------------------------------------------------------------------

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.Configure<GstinOptions>(builder.Configuration.GetSection(GstinOptions.Section));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.Section));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.Section));
builder.Services.Configure<OtpOptions>(builder.Configuration.GetSection(OtpOptions.Section));
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection(SmsOptions.Section));
builder.Services.Configure<SettlementOptions>(builder.Configuration.GetSection(SettlementOptions.Section));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.Section));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.Section));
builder.Services.Configure<JobsOptions>(builder.Configuration.GetSection(JobsOptions.Section));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
var gstinOptions = builder.Configuration.GetSection(GstinOptions.Section).Get<GstinOptions>() ?? new GstinOptions();
var emailOptions = builder.Configuration.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions();
var smsOptions = builder.Configuration.GetSection(SmsOptions.Section).Get<SmsOptions>() ?? new SmsOptions();
var jobsOptions = builder.Configuration.GetSection(JobsOptions.Section).Get<JobsOptions>() ?? new JobsOptions();

if (string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "Jwt:Key is not configured. Set it via the environment before starting the API in Production.");
    }

    // Development convenience only: a stable per-machine key so tokens survive a restart.
    jwtOptions.Key = "saakh-development-signing-key-change-me-0123456789";
    builder.Configuration["Jwt:Key"] = jwtOptions.Key;
}

// ---- database ------------------------------------------------------------------------

// One definition of where the connection string comes from, used by everything
// that needs one. Local development without an explicit setting still works.
static string ConnectionStringFor(IConfiguration configuration) =>
    configuration.GetConnectionString("Default")
    ?? "Server=localhost,1433;Database=Saakh;User=sa;Password=Your_strong_Password1;TrustServerCertificate=True";


builder.Services.AddDbContext<SaakhDbContext>((provider, options) =>
    // SQL Server is the only supported database. Retry-on-failure covers the
    // transient disconnects a containerised SQL Server produces while it starts.
    // The connection string is resolved here rather than captured at startup, so
    // a configuration source registered later is still honoured.
    options.UseSqlServer(
        ConnectionStringFor(provider.GetRequiredService<IConfiguration>()),
        sql => sql.EnableRetryOnFailure()));

// ---- identity + auth -----------------------------------------------------------------

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<SaakhDbContext>()
    .AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Validation is configured from the same bound JwtOptions that JwtTokenService
// signs with, rather than from a value captured earlier during startup. Those
// two can otherwise drift — any configuration source registered after this file
// reads the key would leave the API signing with one secret and validating with
// another, which presents as a blanket 401 and is miserable to diagnose.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        var options = jwt.Value;

        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // SignalR's WebSocket handshake cannot set an Authorization header, so the hub
        // accepts the access token from the query string instead.
        bearer.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ---- app services --------------------------------------------------------------------

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();
builder.Services.AddScoped<IInterestService, InterestService>();
builder.Services.AddScoped<IDealService, DealService>();
builder.Services.AddScoped<IProposalService, ProposalService>();
builder.Services.AddScoped<IRatingService, RatingService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ITrustStatsService, TrustStatsService>();
builder.Services.AddScoped<IRealtimePublisher, RealtimePublisher>();
builder.Services.AddScoped<IFileStorageService, LocalDiskFileStorage>();
builder.Services.AddSingleton<IOtpService, InMemoryOtpService>();
builder.Services.AddSingleton<SchemaReadiness>();
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddScoped<ApprovalEmailJob>();
builder.Services.AddScoped<GstinRetryJob>();
builder.Services.AddScoped<SuspensionExpiryJob>();
builder.Services.AddScoped<SettlementOverdueJob>();

// GSTIN verification: the mock is the default outside Production so automated runs never
// burn the provider's monthly free quota (tech-stack.md).
if (gstinOptions.Provider.Equals("GstinApi", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IGstinVerifier, GstinApiVerifier>(http =>
    {
        http.BaseAddress = new Uri(gstinOptions.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(gstinOptions.TimeoutSeconds);
        if (!string.IsNullOrWhiteSpace(gstinOptions.ApiKey))
        {
            http.DefaultRequestHeaders.Add("x-api-key", gstinOptions.ApiKey);
        }
    });
}
else
{
    builder.Services.AddScoped<IGstinVerifier, MockGstinVerifier>();
}

// How the signup code reaches the user. "Log" delivers nothing and hands the code back
// to the form, which is what makes a fresh clone demoable — and what makes the step
// verify nothing, so a real deployment configures a channel here.
//
// "Email" exists because Indian A2P SMS is DLT-regulated and every gateway gates its
// API behind payment or verification. It is real out-of-band delivery at no cost, and
// it checks an inbox rather than a handset; the signup form says which.
switch (smsOptions.Provider.ToLowerInvariant())
{
    case "email":
        builder.Services.AddScoped<IOtpChannel, EmailOtpChannel>();
        break;

    case "fast2sms":
        builder.Services.AddHttpClient<IOtpChannel, Fast2SmsChannel>(http =>
            http.Timeout = TimeSpan.FromSeconds(smsOptions.TimeoutSeconds));
        break;

    case "msg91":
        builder.Services.AddHttpClient<IOtpChannel, Msg91Channel>(http =>
            http.Timeout = TimeSpan.FromSeconds(smsOptions.TimeoutSeconds));
        break;

    case "twilio":
        builder.Services.AddHttpClient<IOtpChannel, TwilioChannel>(http =>
            http.Timeout = TimeSpan.FromSeconds(smsOptions.TimeoutSeconds));
        break;

    default:
        builder.Services.AddSingleton<IOtpChannel, LoggingOtpChannel>();
        break;
}

builder.Services.AddScoped<IEmailSender>(sp => emailOptions.Provider.ToLowerInvariant() switch
{
    "sendgrid" => ActivatorUtilities.CreateInstance<SendGridEmailSender>(sp),
    "smtp" => ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp),
    _ => ActivatorUtilities.CreateInstance<LoggingEmailSender>(sp)
});

builder.Services.AddValidatorsFromAssemblyContaining<RegisterDtoValidator>();
builder.Services.AddFluentValidationAutoValidation();

// ---- hangfire ------------------------------------------------------------------------

builder.Services.AddHangfire((provider, config) =>
{
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings();

    // Resolved from DI for the same reason as the DbContext above. Capturing the
    // string at startup pointed job storage at whatever was configured on that
    // line, so an enqueue could fail against an unreachable server while every
    // EF query succeeded — a split-brain that is very hard to read from a 500.
    config.UseSqlServerStorage(
        ConnectionStringFor(provider.GetRequiredService<IConfiguration>()),
        new SqlServerStorageOptions
        {
            PrepareSchemaIfNecessary = true,
            QueuePollInterval = TimeSpan.FromSeconds(jobsOptions.QueuePollIntervalSeconds)
        });
});

if (!seedOnly && jobsOptions.Enabled)
{
    builder.Services.AddHangfireServer();
}

// ---- web -----------------------------------------------------------------------------

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums travel as numbers to match the TypeScript enums on the client, and nulls
        // are kept so the Angular models see an explicit absence.
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddSignalR();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // The limits are resolved per request from the bound options rather than
    // captured here. Reading them at startup would freeze whatever configuration
    // happened to be loaded at this line, which silently ignores any source
    // registered later — the same trap that had JWT signing and validation
    // disagreeing.
    static RateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    options.AddPolicy(RateLimitPolicies.Gstin, context =>
    {
        var limits = Limits(context);

        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.GstinPermitLimit,
                Window = TimeSpan.FromMinutes(limits.GstinWindowMinutes)
            });
    });

    options.AddPolicy(RateLimitPolicies.Signup, context =>
    {
        var limits = Limits(context);

        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.SignupPermitLimit,
                Window = TimeSpan.FromMinutes(limits.SignupWindowMinutes)
            });
    });
});

var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                     ?? ["http://localhost:4200", "http://localhost", "http://localhost:80"];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    // Required for the SignalR WebSocket connection.
    .AllowCredentials()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Saakh API",
        Version = "v1",
        Description = "Portable trust records for small vendors. Discovery, deal tracking and ratings."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token returned by /api/auth/login."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    var xml = Path.Combine(AppContext.BaseDirectory, "Saakh.Api.xml");
    if (File.Exists(xml))
    {
        options.IncludeXmlComments(xml);
    }
});

var app = builder.Build();

// ---- seed-only run -------------------------------------------------------------------

if (seedOnly)
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.RunAsync(reset: resetOnSeed);
    return;
}

// ---- pipeline ------------------------------------------------------------------------

using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Saakh.Startup");

    var readiness = app.Services.GetRequiredService<SchemaReadiness>();
    var db = scope.ServiceProvider.GetRequiredService<SaakhDbContext>();

    // Migrations are the deployment path (the `migrate` bundle, or `dotnet ef
    // database update`). Applying them on boot is opt-in because two replicas
    // migrating the same database at once is how a deployment gets lost; unset
    // means Development only, so a fresh clone still runs in one command.
    var databaseOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

    if (databaseOptions.MigrateOnStartup ?? app.Environment.IsDevelopment())
    {
        try
        {
            await db.Database.MigrateAsync();
            startupLogger.LogInformation("Schema is up to date.");
        }
        catch (Exception ex)
        {
            // Reported, not fatal, for the same reason as the role seeding below.
            startupLogger.LogError(ex,
                "Could not apply migrations on startup. The API will start and report itself "
                + "not ready on /health until the schema is in place.");
        }
    }

    // On a first deployment the schema may not exist yet. The API must still
    // start: a service that crash-loops until someone migrates it is impossible
    // to inspect, and its logs are the only place the reason is written. But
    // "started" is not "working", so the result is recorded and /health reports
    // it rather than leaving a warning for somebody to find.
    await readiness.RefreshAsync(db, CancellationToken.None);

    if (readiness.Current.IsReady)
    {
        try
        {
            await DataSeeder.EnsureRolesAsync(scope.ServiceProvider);
        }
        catch (Exception ex)
        {
            startupLogger.LogWarning(ex,
                "Could not create the Lender/Seeker/Admin roles. Signup stays unavailable "
                + "until they exist.");
        }
    }
    else
    {
        startupLogger.LogWarning(
            "Starting without a usable schema ({Database}): {Detail} Signup and every other "
            + "write stays unavailable, and /health reports 503, until this is resolved.",
            readiness.Current.Database, readiness.Current.Detail);
    }
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Saakh API v1"));
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SaakhHub>("/hubs/saakh");

// The Hangfire dashboard is a developer tool, not a product surface, so it is only mapped
// outside Production where it would need its own authorization filter.
if (!app.Environment.IsProduction())
{
    app.MapHangfireDashboard("/hangfire");
}

// Hangfire polls its storage on a fixed interval whether or not there is work, so
// registering the sweeps is what first wakes the database and what keeps it awake.
// An instance that scales to zero, or one on a database metered by time-online
// rather than by query, wants this off — see JobsOptions.Enabled.
if (!jobsOptions.Enabled)
{
    app.Logger.LogWarning(
        "Background jobs are disabled (Jobs:Enabled=false). The GSTIN retry sweep, the "
        + "suspension expiry and the overdue-settlement closure will not run in this instance.");
}
else
{
    // Registered through IRecurringJobManager rather than the static RecurringJob
    // helper: the static one reads JobStorage.Current, which SQL Server storage does
    // not populate until the Hangfire server has started. Like the role seeding
    // above, this writes to the database, so it is tolerant of a schema that has not
    // been migrated yet rather than taking the whole API down with it.
    using var jobScope = app.Services.CreateScope();
    var jobLogger = jobScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Saakh.Startup");

    try
    {
        var recurring = jobScope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        recurring.AddOrUpdate<GstinRetryJob>(
            "gstin-retry-sweep", job => job.SweepAsync(), Cron.Hourly());

        recurring.AddOrUpdate<SuspensionExpiryJob>(
            "suspension-expiry", job => job.RunAsync(), Cron.Hourly());

        // Warns both parties when an agreed settlement date passes, and closes the deal
        // once the grace period has run out.
        recurring.AddOrUpdate<SettlementOverdueJob>(
            "settlement-overdue", job => job.RunAsync(), Cron.Hourly());
    }
    catch (Exception ex)
    {
        jobLogger.LogWarning(ex,
            "Could not register the recurring background jobs — Hangfire storage is unavailable. "
            + "Apply migrations, then restart this service.");
    }
}

// Liveness: the process is up and serving. Deliberately asks nothing of the
// database, so a restarting SQL Server never gets an otherwise-fine API killed.
app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "saakh-api" }))
    .WithTags("Health");

// Readiness: up *and* able to do its job. A missing or out-of-date schema answers
// 503 with the reason and the fix, so an unmigrated deployment shows up in
// `docker compose ps` instead of hiding behind a healthy container that fails
// every write.
app.MapGet("/health", async (SchemaReadiness readiness, SaakhDbContext db, CancellationToken cancellationToken) =>
    {
        var state = await readiness.RefreshAsync(db, cancellationToken);

        return state.IsReady
            ? Results.Ok(new { status = "ok", service = "saakh-api", database = state.Database })
            : Results.Json(
                new
                {
                    status = "degraded",
                    service = "saakh-api",
                    database = state.Database,
                    detail = state.Detail
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    })
    .WithTags("Health");

app.Run();

// Top-level statements compile to an internal Program class. Making it public
// lets WebApplicationFactory boot the real application in integration tests,
// rather than tests re-creating a parallel, subtly different host.
public partial class Program;
