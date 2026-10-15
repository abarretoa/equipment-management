using System.Text.RegularExpressions;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// One isolated PostgreSQL database per integration-test process (ADR-074).
/// The configured connection (<c>ConnectionStrings__Default</c>, or the local
/// docker-compose defaults) is used only as the SERVER location/credentials: its
/// database name is never a destination for test data. On first use this creates
/// <c>equipment_management_tests_&lt;guid&gt;</c>, applies the real migrations to
/// it, and every <see cref="ApiWebApplicationFactory"/> in the process points at
/// it. <see cref="IntegrationTestFramework"/> drops it after the run.
/// </summary>
public static partial class TestDatabase
{
    public const string NamePrefix = "equipment_management_tests_";

    private const string MaintenanceDatabase = "postgres";

    /// <summary>Local docker-compose server (docker-compose.yml) when no ConnectionStrings__Default is set.</summary>
    private const string DefaultServerConnectionString =
        "Host=localhost;Port=5432;Username=equipment_management;Password=equipment_management_dev_only";

    private static readonly Lazy<string> Provisioned = new(Provision, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly object DropLock = new();
    private static bool dropped;

    // Captured before ApiWebApplicationFactory overwrites the environment
    // variable with the test database's connection string.
    private static readonly string SourceConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default") is { Length: > 0 } configured
            ? configured
            : DefaultServerConnectionString;

    /// <summary>The database this run created — the only one test setup may migrate or drop.</summary>
    public static string Name { get; } = NamePrefix + TestRun.Id;

    /// <summary>The database named by the configured connection, if any (e.g. the development database). Never written to.</summary>
    public static string? SourceDatabaseName { get; } = new NpgsqlConnectionStringBuilder(SourceConnectionString).Database;

    /// <summary>Connection string for this run's database; creates and migrates it on first access.</summary>
    public static string ConnectionString => Provisioned.Value;

    public static bool IsCreated => Provisioned.IsValueCreated;

    /// <summary>A fresh, valid PostgreSQL database name (59 characters, under the 63-character limit).</summary>
    public static string GenerateName() => NamePrefix + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Fail-closed check run before any migration or drop: the connection must
    /// target exactly this run's generated database. A name that merely looks
    /// like a test database is not enough.
    /// </summary>
    public static void EnsureIsThisRunsDatabase(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database ?? string.Empty;
        if (!string.Equals(database, Name, StringComparison.Ordinal) ||
            !GeneratedNamePattern().IsMatch(database) ||
            string.Equals(database, SourceDatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to run destructive integration-test setup against database '{database}': " +
                $"only this run's generated test database '{Name}' is allowed.");
        }
    }

    /// <summary>Drops this run's database (only ever <see cref="Name"/>). Idempotent; a no-op if it was never created.</summary>
    public static void DropIfCreated()
    {
        lock (DropLock)
        {
            if (dropped || !Provisioned.IsValueCreated)
            {
                return;
            }

            dropped = true;
            EnsureIsThisRunsDatabase(Provisioned.Value);
            Drop();
        }
    }

    private static string Provision()
    {
        var testConnectionString = new NpgsqlConnectionStringBuilder(SourceConnectionString) { Database = Name }.ConnectionString;
        EnsureIsThisRunsDatabase(testConnectionString);

        ExecuteOnMaintenanceDatabase($"""CREATE DATABASE "{Name}" """);
        Log($"Test database created: {Name}");

        try
        {
            // Schema exists before any test host starts, so classes outside the
            // serialized Phase1 collection never race the first migration.
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(testConnectionString, npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
                .Options;
            using var dbContext = new ApplicationDbContext(options);
            dbContext.Database.Migrate();
        }
        catch
        {
            // The Lazy caches this failure (every test fails closed), and
            // DropIfCreated never sees a created value — so clean up here.
            Drop();
            throw;
        }

        // Best effort only (the VSTest host often exits without running it); the
        // real teardown is IntegrationTestFramework's assembly runner.
        // DropIfCreated is idempotent.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DropIfCreated();

        return testConnectionString;
    }

    private static void Drop()
    {
        // Pooled idle connections from every test host would otherwise keep the
        // database busy; WITH (FORCE) then ends any that remain on THIS database
        // only. Only ever Name — there is no prefix-matching cleanup.
        NpgsqlConnection.ClearAllPools();
        ExecuteOnMaintenanceDatabase($"""DROP DATABASE IF EXISTS "{Name}" WITH (FORCE)""");
        Log($"Test database dropped: {Name}");
    }

    private static void ExecuteOnMaintenanceDatabase(string sql)
    {
        // Name is interpolated into DDL (identifiers can't be parameters), so it
        // must be our own generated value — never anything caller-supplied.
        if (!GeneratedNamePattern().IsMatch(Name))
        {
            throw new InvalidOperationException("Generated test database name is malformed.");
        }

        var maintenance = new NpgsqlConnectionStringBuilder(SourceConnectionString)
        {
            Database = MaintenanceDatabase,
            Pooling = false,
        };

        using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static void Log(string message) => IntegrationTestFramework.Log(message);

    [GeneratedRegex("^equipment_management_tests_[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedNamePattern();
}
