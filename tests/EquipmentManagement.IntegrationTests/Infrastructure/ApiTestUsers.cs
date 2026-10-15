using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Creates/inspects internal users directly through <see cref="UserManager{TUser}"/>
/// (bypassing HTTP) for test setup — e.g. "log in as an existing Administration
/// user" needs that user to already exist. Every created user gets a unique
/// synthetic @example.invalid email so parallel/sequential test runs never
/// collide on the duplicate-email rule (Phase 1 scope: never real emails/credentials in tests).
/// </summary>
public static class ApiTestUsers
{
    public const string DefaultPassword = "Str0ng!TestPassw0rd#2026";

    public static async Task<(string Email, string Password)> CreateAsync(
        ApiWebApplicationFactory factory, string role, string? address = null, bool isActive = true)
    {
        var email = $"{role.ToLowerInvariant()}.{Guid.NewGuid():N}@example.invalid";

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Test",
            LastName = role,
            PhoneNumber = "+351900000000",
            Address = address,
            IsActive = isActive,
            MustChangePassword = false,
        };

        var result = await userManager.CreateAsync(user, DefaultPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create test user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(user, role);

        return (email, DefaultPassword);
    }

    public static async Task SetActiveAsync(ApiWebApplicationFactory factory, string email, bool isActive)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email) ?? throw new InvalidOperationException("Test user not found.");
        user.IsActive = isActive;
        await userManager.UpdateAsync(user);
    }

    public static async Task<Guid> GetIdAsync(ApiWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email) ?? throw new InvalidOperationException("Test user not found.");
        return user.Id;
    }

    /// <summary>
    /// Active Administration users other than <paramref name="excludingEmail"/>,
    /// across the whole shared database — used to make the last-Administration
    /// safeguard test deterministic regardless of what other tests have created.
    /// </summary>
    public static async Task<List<string>> GetOtherActiveAdministratorEmailsAsync(ApiWebApplicationFactory factory, string excludingEmail)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admins = await userManager.GetUsersInRoleAsync(Roles.Administration);
        return admins
            .Where(u => u.IsActive && !string.Equals(u.Email, excludingEmail, StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Email!)
            .ToList();
    }
}
