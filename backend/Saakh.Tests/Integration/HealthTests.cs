using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Saakh.Tests.Integration;

/// <summary>
/// The API starts on purpose even when its schema is missing, so that the reason
/// can be read rather than guessed at from a crash loop. That only works if
/// "started" and "ready" are different answers — otherwise an unmigrated
/// deployment sits there looking healthy while every write fails.
/// </summary>
[Collection(ApiCollection.Name)]
public class HealthTests
{
    private readonly SaakhApiFactory _factory;

    public HealthTests(SaakhApiFactory factory) => _factory = factory;

    private record HealthPayload(string Status, string Service, string? Database, string? Detail);

    [Fact]
    public async Task Readiness_reports_the_schema_state_and_needs_no_token()
    {
        var response = await _factory.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();

        payload!.Status.Should().Be("ok");
        payload.Database.Should().Be("ready", "this database is migrated to head");
    }

    [Fact]
    public async Task Liveness_answers_without_asking_the_database_anything()
    {
        // Separate from readiness so a restarting SQL Server cannot get an
        // otherwise-healthy API killed by its orchestrator.
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<HealthPayload>())!.Status.Should().Be("ok");
    }
}
