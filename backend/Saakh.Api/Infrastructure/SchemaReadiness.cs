using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;

namespace Saakh.Api.Infrastructure;

/// <summary>
/// Whether the database this instance is pointed at actually has the schema the
/// code expects.
///
/// The API deliberately starts even when it does not — a service that crash-loops
/// until someone migrates it cannot be inspected, and the logs are the only place
/// to read the reason. But "started" is not "working": with no schema, signup and
/// every other write fails. So readiness is a first-class, queryable fact rather
/// than a warning somebody has to go looking for, and <c>/health</c> reports it.
/// </summary>
public class SchemaReadiness
{
    private volatile State current = new(false, "unknown", "The schema has not been checked yet.");

    public record State(bool IsReady, string Database, string Detail);

    public State Current => current;

    public void MarkReady() =>
        current = new State(true, "ready", "All migrations are applied.");

    public void MarkPending(IReadOnlyCollection<string> pending) =>
        current = new State(
            false,
            "migrations-pending",
            $"{pending.Count} migration(s) have not been applied, starting with "
            + $"{pending.First()}. Apply them with the migrate bundle, then this instance "
            + "reports ready without a restart.");

    public void MarkUnreachable(Exception error) =>
        current = new State(
            false,
            "unreachable",
            $"The database could not be reached: {error.Message}");

    /// <summary>
    /// Re-checks a database that was not ready last time. Once it is ready the
    /// answer is cached, so the health endpoint costs nothing on the normal path
    /// and a schema applied after boot is still picked up without a restart.
    /// </summary>
    public async Task<State> RefreshAsync(SaakhDbContext db, CancellationToken cancellationToken)
    {
        if (current.IsReady)
        {
            return current;
        }

        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            if (pending.Count == 0)
            {
                MarkReady();
            }
            else
            {
                MarkPending(pending);
            }
        }
        catch (Exception ex)
        {
            MarkUnreachable(ex);
        }

        return current;
    }
}
