using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Runs once before any Phase 1 auth/user/audit test: applies migrations and
/// seeds roles + permission claims against this run's isolated test database
/// (<see cref="TestDatabase"/>, already migrated when it was created; the
/// migration here is then a no-op) (Program.cs deliberately skips this in the "Testing" environment — see its
/// comment — so tests that need it do it explicitly, same pattern as
/// IdentitySeedingTests).
/// </summary>
public sealed class Phase1SchemaFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var factory = new ApiWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        TestDatabase.EnsureIsThisRunsDatabase(dbContext.Database.GetConnectionString()!);
        await dbContext.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Every test class that creates/modifies users shares this collection so xUnit
/// never runs them concurrently against each other — several tests (notably the
/// last-Administration safeguard) reason about the global count of active
/// Administration users in the shared database and would be flaky under
/// concurrent mutation from a sibling test class. HealthEndpointsTests and
/// IdentitySeedingTests are unaffected (different collection, don't touch Users).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Phase1CollectionDefinition : ICollectionFixture<Phase1SchemaFixture>
{
    public const string Name = "Phase1";
}
