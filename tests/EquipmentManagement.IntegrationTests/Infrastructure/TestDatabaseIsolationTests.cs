using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Proves the integration suite runs against this run's generated database and
/// that destructive setup refuses any other database (ADR-074). Read-only: no
/// test here creates, drops or writes to a database.
/// </summary>
public sealed class TestDatabaseIsolationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public void GeneratedName_IsAValidPrefixedPostgresIdentifier_DistinctFromTheSourceDatabase()
    {
        Assert.Matches("^equipment_management_tests_[0-9a-f]{32}$", TestDatabase.Name);
        Assert.True(TestDatabase.Name.Length <= 63);
        Assert.NotEqual(TestDatabase.SourceDatabaseName, TestDatabase.Name);
    }

    [Fact]
    public void GenerateName_NeverRepeats()
    {
        var names = Enumerable.Range(0, 1000).Select(_ => TestDatabase.GenerateName()).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ApplicationUnderTest_IsConnectedToThisRunsDatabase()
    {
        using var scope = factory.Services.CreateScope();
        var configured = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Default");
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(TestDatabase.Name, new NpgsqlConnectionStringBuilder(configured).Database);
        Assert.Equal(TestDatabase.Name, new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database);

        var currentDatabase = await db.Database.SqlQueryRaw<string>("SELECT current_database() AS \"Value\"").SingleAsync();
        Assert.Equal(TestDatabase.Name, currentDatabase);
        Assert.NotEqual(TestDatabase.SourceDatabaseName, currentDatabase);
    }

    [Fact]
    public void Guard_AcceptsThisRunsDatabase()
    {
        TestDatabase.EnsureIsThisRunsDatabase(ApiWebApplicationFactory.ConnectionString);
    }

    [Theory]
    [InlineData("equipment_management")]
    [InlineData("equipment_management_ci")]
    [InlineData("equipment_management_demo")]
    [InlineData("postgres")]
    [InlineData("equipment_management_tests")]
    [InlineData("my_test_db")]
    public void Guard_RejectsAnyOtherDatabase(string database)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ApiWebApplicationFactory.ConnectionString) { Database = database }.ConnectionString;

        Assert.Throws<InvalidOperationException>(() => TestDatabase.EnsureIsThisRunsDatabase(connectionString));
    }

    [Fact]
    public void Guard_RejectsAnotherWellFormedTestDatabaseName()
    {
        // Looks exactly like a test database, but is not the one this run created.
        var connectionString = new NpgsqlConnectionStringBuilder(ApiWebApplicationFactory.ConnectionString)
        {
            Database = TestDatabase.GenerateName(),
        }.ConnectionString;

        Assert.Throws<InvalidOperationException>(() => TestDatabase.EnsureIsThisRunsDatabase(connectionString));
    }

    [Fact]
    public void Guard_RejectsTheConfiguredSourceDatabase()
    {
        if (TestDatabase.SourceDatabaseName is not { Length: > 0 } source)
        {
            return; // No database named in the configured connection: nothing to reject.
        }

        var connectionString = new NpgsqlConnectionStringBuilder(ApiWebApplicationFactory.ConnectionString) { Database = source }.ConnectionString;

        Assert.Throws<InvalidOperationException>(() => TestDatabase.EnsureIsThisRunsDatabase(connectionString));
    }
}
