using FluentAssertions;
using Saakh.Api.Infrastructure;
using Xunit;

namespace Saakh.Tests.Unit;

/// <summary>
/// Whatever the health endpoint says is the only thing an operator will read at
/// 2am, so each not-ready state has to name both the cause and the fix.
/// </summary>
public class SchemaReadinessTests
{
    [Fact]
    public void An_unchecked_database_is_not_treated_as_ready()
    {
        // Fail closed: nothing has looked at the schema yet, so it cannot be
        // reported as working.
        new SchemaReadiness().Current.IsReady.Should().BeFalse();
    }

    [Fact]
    public void A_pending_migration_names_the_first_one_and_says_how_to_apply_it()
    {
        var readiness = new SchemaReadiness();

        readiness.MarkPending(["20260101_AddProfiles", "20260202_AddDeals"]);

        var state = readiness.Current;
        state.IsReady.Should().BeFalse();
        state.Database.Should().Be("migrations-pending");
        state.Detail.Should().Contain("2 migration");
        state.Detail.Should().Contain("20260101_AddProfiles");
        state.Detail.Should().Contain("migrate bundle");
    }

    [Fact]
    public void An_unreachable_database_reports_the_underlying_error()
    {
        var readiness = new SchemaReadiness();

        readiness.MarkUnreachable(new InvalidOperationException("server was not found"));

        readiness.Current.Database.Should().Be("unreachable");
        readiness.Current.Detail.Should().Contain("server was not found");
    }

    [Fact]
    public void Readiness_is_recoverable_without_a_restart()
    {
        // A schema applied after the API booted has to flip this to ready, or
        // the documented "migrate, then it reports ready" is a lie.
        var readiness = new SchemaReadiness();
        readiness.MarkUnreachable(new InvalidOperationException("down"));

        readiness.MarkReady();

        readiness.Current.IsReady.Should().BeTrue();
        readiness.Current.Database.Should().Be("ready");
    }
}
