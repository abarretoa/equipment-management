namespace EquipmentManagement.Infrastructure.Users;

/// <summary>Input to <see cref="UserManagementService.CreateAsync"/>.</summary>
public sealed record CreateUserData(
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string Role,
    string? Address);

/// <summary>
/// Input to <see cref="UserManagementService.UpdateAsync"/>. Email and role are
/// deliberately excluded — role changes go through
/// <see cref="UserManagementService.ChangeRoleAsync"/>, which carries its own
/// last-Administration safeguard, and email changes are out of Phase 1 scope.
/// </summary>
public sealed record UpdateUserData(
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Address,
    string? ExpectedConcurrencyStamp);

/// <summary>
/// Input to <see cref="UserManagementService.UpdateOwnProfileAsync"/> — the
/// self-service subset of <see cref="UpdateUserData"/>. No user id, email, role,
/// activation or password field exists here by design (ADR-073): the target is
/// always the signed-in user, and nothing else on the account is reachable.
/// </summary>
public sealed record UpdateOwnProfileData(
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Address);

public sealed record ChangeRoleData(string Role, string? ExpectedConcurrencyStamp);

public sealed record ActivationChangeData(string? ExpectedConcurrencyStamp);

/// <summary>Server-side pagination + optional filters for <see cref="UserManagementService.ListAsync"/>.</summary>
public sealed record UserListFilter(string? Search, string? Role, bool? IsActive);
