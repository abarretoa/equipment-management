using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests;

/// <summary>
/// Exercises the Phase 0 Identity foundation against a real PostgreSQL database:
/// migrations apply cleanly and role seeding is idempotent. Runs against this
/// run's isolated <see cref="TestDatabase"/> on the configured PostgreSQL server
/// (docker-compose locally, a service container in CI) — see README "Running tests".
/// </summary>
public sealed class IdentitySeedingTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task SeedAsync_CreatesBothRoles_AndIsIdempotent()
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;

        var dbContext = provider.GetRequiredService<ApplicationDbContext>();
        TestDatabase.EnsureIsThisRunsDatabase(dbContext.Database.GetConnectionString()!);
        await dbContext.Database.MigrateAsync();

        var seeder = provider.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync();
        await seeder.SeedAsync(); // must not throw or duplicate on a second run

        var roleManager = provider.GetRequiredService<RoleManager<ApplicationRole>>();
        Assert.True(await roleManager.RoleExistsAsync(Roles.Administration));
        Assert.True(await roleManager.RoleExistsAsync(Roles.HR));
    }
}
