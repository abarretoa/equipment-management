using System.ComponentModel.DataAnnotations;

namespace EquipmentManagement.Api.Contracts.Auth;

/// <summary>
/// Login input. Deliberately just credentials — no role/permissions field, which
/// would let a caller attempt to influence authorization from the request body
/// (Phase 1 scope).
/// </summary>
public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

/// <summary>
/// The password policy itself (minimum 12 characters, digit, upper- and
/// lower-case letter) is enforced by ASP.NET Core Identity, whose messages are
/// pt-PT via <c>PortugueseIdentityErrorDescriber</c>.
/// </summary>
public sealed record ChangePasswordRequest(
    [Required(ErrorMessage = "Indique a palavra-passe atual.")] string CurrentPassword,
    [Required(ErrorMessage = "Indique a nova palavra-passe.")] string NewPassword);

/// <summary>
/// The safe profile shape returned by both login and <c>GET /api/auth/me</c>.
/// Never includes password hashes, security stamps, or other Identity internals
/// (Phase 1 scope).
/// </summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    IReadOnlyCollection<string> Permissions,
    bool MustChangePassword);
