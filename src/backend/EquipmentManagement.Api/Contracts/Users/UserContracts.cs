using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Domain.Users;

namespace EquipmentManagement.Api.Contracts.Users;

/// <summary>
/// User creation input. No <c>permissions</c> field: permissions derive entirely
/// from <c>Role</c> via the central role-permission mapping, never from caller
/// input — this is what prevents a privilege-escalation request
/// (Phase 1 scope). Address is
/// optional here at the wire level because it's only conditionally required
/// (HR role) — see <c>Domain.Users.UserProfileRules</c> for the actual rule,
/// enforced server-side in <c>UserManagementService</c>. Phone is optional
/// (<see cref="OptionalPhoneAttribute"/>); limits come from
/// <see cref="UserProfileRules"/>, the same values the database columns use.
/// </summary>
public sealed record CreateUserRequest(
    [Required(ErrorMessage = UserValidationMessages.FirstNameRequired), MaxLength(UserProfileRules.MaxNameLength, ErrorMessage = UserValidationMessages.FirstNameTooLong)] string FirstName,
    [Required(ErrorMessage = UserValidationMessages.LastNameRequired), MaxLength(UserProfileRules.MaxNameLength, ErrorMessage = UserValidationMessages.LastNameTooLong)] string LastName,
    [Required(ErrorMessage = UserValidationMessages.EmailRequired), EmailAddress(ErrorMessage = UserValidationMessages.EmailInvalid)] string Email,
    [OptionalPhone] string? Phone,
    [Required(ErrorMessage = UserValidationMessages.RoleRequired)] string Role,
    [MaxLength(UserProfileRules.MaxAddressLength, ErrorMessage = UserValidationMessages.AddressTooLong)] string? Address);

/// <summary>
/// Profile update input. Email and Role are deliberately absent: email changes
/// are out of Phase 1 scope, and role changes go through the dedicated
/// <c>PUT /api/users/{id}/role</c> endpoint with its own last-Administration
/// safeguard.
/// </summary>
public sealed record UpdateUserRequest(
    [Required(ErrorMessage = UserValidationMessages.FirstNameRequired), MaxLength(UserProfileRules.MaxNameLength, ErrorMessage = UserValidationMessages.FirstNameTooLong)] string FirstName,
    [Required(ErrorMessage = UserValidationMessages.LastNameRequired), MaxLength(UserProfileRules.MaxNameLength, ErrorMessage = UserValidationMessages.LastNameTooLong)] string LastName,
    [OptionalPhone] string? Phone,
    [MaxLength(UserProfileRules.MaxAddressLength, ErrorMessage = UserValidationMessages.AddressTooLong)] string? Address,
    string? ConcurrencyStamp);

/// <summary>pt-PT messages for the user/profile request validation attributes.</summary>
public static class UserValidationMessages
{
    public const string FirstNameRequired = "Indique o nome próprio.";
    public const string FirstNameTooLong = "O nome próprio não pode ter mais de 200 caracteres.";
    public const string LastNameRequired = "Indique o apelido.";
    public const string LastNameTooLong = "O apelido não pode ter mais de 200 caracteres.";
    public const string EmailRequired = "Indique o e-mail.";
    public const string EmailInvalid = "Indique um e-mail válido.";
    public const string RoleRequired = "Indique a função.";
    public const string AddressTooLong = "A morada não pode ter mais de 500 caracteres.";
}

public sealed record ChangeUserRoleRequest([Required(ErrorMessage = UserValidationMessages.RoleRequired)] string Role, string? ConcurrencyStamp);

public sealed record ActivationRequest(string? ConcurrencyStamp);

/// <summary>
/// The safe, wire-facing shape of an internal user — never the
/// <c>ApplicationUser</c> Identity entity (docs/PROJECT_SPEC.md, section 24).
/// </summary>
public sealed record UserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string? Address,
    string Role,
    bool IsActive,
    bool MustChangePassword,
    string ConcurrencyStamp);

/// <summary>
/// Returned once, immediately after creation. The temporary password appears
/// nowhere else — not logged, not persisted in plaintext, not retrievable again
/// (Phase 1 scope). Administration
/// must communicate it to the new user through some out-of-band channel.
/// </summary>
public sealed record CreatedUserResponse(UserResponse User, string TemporaryPassword);

public sealed record ResetPasswordResponse(string TemporaryPassword);
