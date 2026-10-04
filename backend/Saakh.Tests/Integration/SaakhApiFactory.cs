using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Saakh.Api.Data;
using Testcontainers.MsSql;
using Xunit;

namespace Saakh.Tests.Integration;

/// <summary>
/// Boots the real application against a real SQL Server started in Docker.
///
/// Deliberately not an in-memory or SQLite substitute. Three of the four bugs
/// that reached this project were invisible outside a Linux container talking to
/// SQL Server: a globalization setting that broke the SQL client, a startup that
/// queried an un-migrated schema, and a foreign-key cascade rule that only SQL
/// Server enforces. A test double would have passed all three.
/// </summary>
public class SaakhApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Saakh_Test_Password1!")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        // Point at a database of our own rather than the container's default, so the
        // migration has to create it exactly as it would on a fresh deployment.
        ConnectionString = _sql.GetConnectionString().Replace("Database=master", "Database=SaakhTests");

        // Migrate through a standalone context, before the host is ever touched.
        // This mirrors the real deployment order — migrate, then start the service —
        // and it matters here: booting the app first would have Hangfire try to
        // prepare its schema in a database that does not exist yet, which leaves
        // job storage unusable and every enqueue failing with a 500.
        var options = new DbContextOptionsBuilder<SaakhDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using (var migrationContext = new SaakhDbContext(options))
        {
            // The committed migrations, applied to real SQL Server. If the schema
            // contains a cascade cycle, this is where it fails — loudly, in CI.
            await migrationContext.Database.MigrateAsync();
        }

        using var scope = Services.CreateScope();
        await DataSeeder.EnsureRolesAsync(scope.ServiceProvider);
    }

    public new async Task DisposeAsync()
    {
        await _sql.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Staging);

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Jwt:Key"] = "integration-test-signing-key-at-least-32-characters-long",
                // Mock verification and a logging mail sender: a test run must never
                // spend a real GSTIN credit or send mail to a real inbox.
                ["Gstin:Provider"] = "Mock",
                ["Email:Provider"] = "Log",
                // The suite registers far more accounts from one address than a
                // real signup flow ever would. The limiter is exercised on its
                // own terms elsewhere, not incidentally by every other test.
                ["RateLimits:SignupPermitLimit"] = "100000",
                ["RateLimits:GstinPermitLimit"] = "100000"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Re-point the context at the container, replacing the registration the
            // application made from its own configuration.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<SaakhDbContext>));

            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<SaakhDbContext>(options => options.UseSqlServer(ConnectionString));
        });
    }

    /// <summary>A scope holding a context bound to the test database.</summary>
    public async Task WithDbAsync(Func<SaakhDbContext, Task> work)
    {
        using var scope = Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<SaakhDbContext>());
    }
}

/// <summary>
/// One SQL Server container shared by every integration test, because starting
/// one costs tens of seconds and the tests do not need isolation from each other
/// beyond using distinct accounts.
/// </summary>
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<SaakhApiFactory>
{
    public const string Name = "saakh-api";
}
