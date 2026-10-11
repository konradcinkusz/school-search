using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Tests.Migrations;

/// <summary>
/// A fact that runs only when <c>CATALOG_TEST_PG</c> holds an Npgsql connection string.
/// When the variable is unset the test is reported as <b>skipped</b> (never as passed).
/// </summary>
public sealed class RequiresCatalogPostgresFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "CATALOG_TEST_PG";

    public RequiresCatalogPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            Skip = $"Set {ConnectionStringVariable} to an Npgsql connection string of an EMPTY database to run this test.";
        }
    }
}

/// <summary>
/// Applies the provider-specific PostgreSQL migrations to an empty database and checks the result.
/// Runs in CI through the postgres:17 service container (ci.yml, job "dotnet").
/// Destructive to the target database only in the sense that it expects it to be empty.
/// </summary>
public sealed class PostgresMigrationTests
{
    private const string TablesSql =
        "SELECT table_name AS \"Value\" FROM information_schema.tables " +
        "WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'";

    [RequiresCatalogPostgresFact]
    public async Task MigrateAsync_on_empty_database_applies_latest_migration_and_creates_tables()
    {
        var connectionString = Environment.GetEnvironmentVariable(RequiresCatalogPostgresFactAttribute.ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("CATALOG_TEST_PG must be set for this test.");
        }

        var builder = new DbContextOptionsBuilder<CatalogDbContext>();
        builder.UseNpgsql(connectionString);

        await using var db = new CatalogDbContext(builder.Options);

        var tablesBefore = await db.Database.SqlQueryRaw<string>(TablesSql).ToListAsync();
        Assert.True(
            tablesBefore.Count == 0,
            "The target database must be empty before the migration test. Found: " + string.Join(", ", tablesBefore));

        await db.Database.MigrateAsync();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Contains(CatalogSchema.LatestMigrationId, applied);

        var tables = await db.Database.SqlQueryRaw<string>(TablesSql).ToListAsync();

        // Expected tables come from the model, so a model entity without a migration fails here.
        var expectedTables = db.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Contains("Schools", expectedTables);
        foreach (var table in expectedTables)
        {
            Assert.Contains(table, tables);
        }

        var synthetic = new School
        {
            IdPodmiot = 990001,
            Name = "Szkola Podstawowa nr 1 w Testowie",
            Slug = "szkola-podstawowa-nr-1-w-testowie-migration-test",
            IsPublic = true,
            Provenance = new Provenance
            {
                Source = "synthetic-test-fixture",
                SourceUrl = "https://example.invalid/synthetic",
                AsOf = new DateOnly(2026, 1, 1),
                Method = "Synthetic row inserted by PostgresMigrationTests.",
            },
        };

        try
        {
            db.Schools.Add(synthetic);
            await db.SaveChangesAsync();

            var stored = await db.Schools.AsNoTracking().SingleAsync(s => s.IdPodmiot == synthetic.IdPodmiot);
            Assert.Equal(synthetic.Slug, stored.Slug);
            Assert.Equal("synthetic-test-fixture", stored.Provenance.Source);
        }
        finally
        {
            await db.Schools.Where(s => s.IdPodmiot == synthetic.IdPodmiot).ExecuteDeleteAsync();
        }
    }
}
