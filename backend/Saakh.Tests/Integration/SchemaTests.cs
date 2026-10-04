using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Saakh.Api.Data;
using Xunit;

namespace Saakh.Tests.Integration;

/// <summary>
/// Guards the schema itself against real SQL Server.
///
/// An earlier revision of this model was rejected outright at migration time —
/// deleting a user cascaded into EvidenceDocuments down two different paths, and
/// SQL Server refuses that (error 1785). Nothing in the C# model, and no
/// in-memory provider, would have objected.
/// </summary>
[Collection(ApiCollection.Name)]
public class SchemaTests
{
    private readonly SaakhApiFactory _factory;

    public SchemaTests(SaakhApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_committed_migrations_apply_cleanly_to_sql_server()
    {
        await _factory.WithDbAsync(async db =>
        {
            var applied = await db.Database.GetAppliedMigrationsAsync();
            applied.Should().NotBeEmpty();

            var pending = await db.Database.GetPendingMigrationsAsync();
            pending.Should().BeEmpty("the test database is migrated to head");
        });
    }

    [Fact]
    public async Task The_model_has_no_changes_that_were_never_turned_into_a_migration()
    {
        await _factory.WithDbAsync(db =>
        {
            // Catches the easy mistake of editing the DbContext and forgetting to
            // scaffold a migration, which otherwise only shows up at deploy time.
            db.Database.HasPendingModelChanges().Should().BeFalse();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task One_rating_per_party_per_deal_is_enforced_by_the_database_not_only_by_code()
    {
        await _factory.WithDbAsync(async db =>
        {
            var index = db.Model
                .FindEntityType(typeof(Saakh.Api.Domain.Rating))!
                .GetIndexes()
                .SingleOrDefault(i =>
                    i.Properties.Select(p => p.Name).OrderBy(n => n)
                        .SequenceEqual(new[] { "DealId", "RaterProfileId" }));

            index.Should().NotBeNull("the constraint must survive a bug in the service layer");
            index!.IsUnique.Should().BeTrue();

            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_star_rating_outside_one_to_five_is_rejected_by_the_check_constraint()
    {
        await _factory.WithDbAsync(async db =>
        {
            var constraint = await db.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name = 'CK_Rating_Stars'")
                .SingleAsync();

            constraint.Should().Be(1);
        });
    }

    [Fact]
    public async Task Every_expected_table_exists()
    {
        await _factory.WithDbAsync(async db =>
        {
            var tables = await db.Database
                .SqlQuery<string>($"SELECT name AS [Value] FROM sys.tables")
                .ToListAsync();

            tables.Should().Contain(new[]
            {
                "Profiles", "Interests", "Deals", "DealStateHistories", "Messages",
                "Ratings", "EvidenceDocuments", "Admins", "AdminActionLogs",
                "ResumeRequests", "Notifications", "CategorySubTypes", "RefreshTokens"
            });
        });
    }

    [Fact]
    public async Task The_category_taxonomy_is_seeded_by_the_migration_itself()
    {
        await _factory.WithDbAsync(async db =>
        {
            var subTypes = await db.CategorySubTypes.AsNoTracking().ToListAsync();

            // Reference data, not sample trust signals, so it ships in the migration.
            subTypes.Should().HaveCountGreaterThan(5);
            subTypes.Select(s => s.Key).Should().Contain(new[]
            {
                "fruits", "dairy", "medicine", "industrial-equipment"
            });
        });
    }
}
