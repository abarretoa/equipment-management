using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Api.Contracts.Users;
using EquipmentManagement.Domain.Users;

namespace EquipmentManagement.Api.Contracts.Profile;

/// <summary>
/// The signed-in user's own profile (<c>GET /api/profile</c>, ADR-073). Field
/// names follow <c>UserResponse</c>. No id, concurrency stamp, permissions,
/// IsActive or MustChangePassword: the screen needs none of them, and the
/// session-level ones already come from <c>GET /api/auth/me</c>.
/// </summary>
public sealed record ProfileResponse(
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string? Address,
    string Role);

/// <summary>
/// Self-service profile update (<c>PUT /api/profile</c>, ADR-073). Only the four
/// editable fields exist here — an <c>email</c>, <c>role</c>, <c>id</c> or any
/// other property in the JSON body has nothing to bind to and is ignored. Same
/// validation attributes and messages as <c>UpdateUserRequest</c> (optional
/// phone, 500-character address). The HR address requirement is enforced by
/// <c>UserManagementService</c> against the user's current role, as for
/// Administration edits.
/// </summary>
public sealed record UpdateProfileRequest(
    [Required(ErrorMessage = UserValidationMessages.FirstNameRequired), MaxLength(UserProfileRules.MaxNameLength, ErrorMessage = UserValidationMessages.FirstNameTooLong)] string FirstName,
    [Required(ErrorMessage = UserValidationMessages.LastNameRequired), MaxLength(UserProfileRules.MaxNameLength, ErrorMessage = UserValidationMessages.LastNameTooLong)] string LastName,
    [OptionalPhone] string? Phone,
    [MaxLength(UserProfileRules.MaxAddressLength, ErrorMessage = UserValidationMessages.AddressTooLong)] string? Address);
