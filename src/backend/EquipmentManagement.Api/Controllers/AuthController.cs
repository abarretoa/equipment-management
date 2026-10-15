using System.Security.Claims;
using EquipmentManagement.Api.Contracts.Auth;
using EquipmentManagement.Api.Filters;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EquipmentManagement.Api.Controllers;

/// <summary>
/// Session lifecycle for internal users: login, logout, current-session
/// introspection and self-service password change
/// (Phase 1 scope).
/// Consultants never reach this controller — they have no login (docs/PROJECT_SPEC.md, section 4).
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext,
    AuditService auditService)
    : ControllerBase
{
    /// <summary>
    /// Every failure — unknown email, wrong password, locked-out account,
    /// deactivated account — returns this exact generic message, to avoid
    /// leaking which of those is true (Phase 1 scope). The real reason is still recorded in the audit
    /// log for Administration's own forensic use (Audit.Read is not exposed to
    /// the caller of this endpoint).
    /// </summary>
    public const string GenericLoginFailureMessage = "E-mail ou palavra-passe inválidos.";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ValidateCsrf]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        // Looked up separately, purely to enrich the audit record with whether an
        // account existed at all; the actual sign-in decision below never
        // branches on this, so there is no timing/logic difference between
        // "unknown email" and "wrong password" beyond what PasswordSignInAsync
        // itself already does internally (a deliberate defence against account enumeration).
        var existingUser = await userManager.FindByEmailAsync(request.Email);

        var result = await signInManager.PasswordSignInAsync(
            request.Email, request.Password, isPersistent: false, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            var reason = result.IsLockedOut ? "LockedOut"
                : result.IsNotAllowed ? "AccountInactive"
                : existingUser is null ? "UnknownEmail"
                : "InvalidPassword";

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.Auth.LoginFailed,
                EntityType: "User",
                EntityId: existingUser?.Id.ToString(),
                ActorUserId: null,
                ActorDisplayNameSnapshot: null,
                Metadata: new { attemptedEmail = request.Email, reason }), cancellationToken);

            return Unauthorized(new ProblemDetails { Title = GenericLoginFailureMessage, Status = StatusCodes.Status401Unauthorized });
        }

        var user = existingUser ?? await userManager.FindByEmailAsync(request.Email) ??
            throw new InvalidOperationException("Sign-in succeeded but the user could not be reloaded.");

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.Auth.LoginSucceeded,
            EntityType: "User",
            EntityId: user.Id.ToString(),
            ActorUserId: user.Id,
            ActorDisplayNameSnapshot: $"{user.FirstName} {user.LastName}"), cancellationToken);

        return Ok(await BuildCurrentUserResponseAsync(user));
    }

    /// <summary>
    /// Logout revokes every session of this user, not only the calling browser's
    /// (approved V1 policy). Rotating the security stamp is what invalidates
    /// them: <see cref="ActiveUserCookieValidator"/> compares the cookie's stamp
    /// with the database on every request, so any previously-issued cookie —
    /// a copy of this one or another browser's — is rejected on its very next
    /// request. Only reached after [Authorize] and [ValidateCsrf] pass, so a
    /// rejected logout revokes nothing.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ValidateCsrf]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var user = await userManager.FindByIdAsync(userId.ToString());

        if (user is not null)
        {
            // Fails closed: never report a successful logout while the user's
            // existing sessions knowingly remain valid.
            var revoked = await userManager.UpdateSecurityStampAsync(user);
            if (!revoked.Succeeded)
            {
                throw new InvalidOperationException("Logout could not revoke the user's sessions.");
            }
        }

        await signInManager.SignOutAsync();

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.Auth.Logout,
            EntityType: "User",
            EntityId: userId.ToString(),
            ActorUserId: userId,
            ActorDisplayNameSnapshot: user is null ? null : $"{user.FirstName} {user.LastName}"), cancellationToken);

        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await userManager.FindByIdAsync(GetUserId().ToString());
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(await BuildCurrentUserResponseAsync(user));
    }

    [HttpPost("change-password")]
    [Authorize]
    [ValidateCsrf]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(GetUserId().ToString());
        if (user is null)
        {
            return Unauthorized();
        }

        // Unlike login/logout telemetry, this is a credential mutation: the new
        // password, MustChangePassword=false and the audit row commit together
        // (ADR-076).
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded)
            {
                // Keyed by the request field each error is about, decided by
                // Identity's stable error code (never by the translated text), so
                // the client can show it under the right input.
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(ChangePasswordErrorField(error.Code), error.Description);
                }

                return ValidationProblem();
            }

            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.Auth.PasswordChanged,
                EntityType: "User",
                EntityId: user.Id.ToString(),
                ActorUserId: user.Id,
                ActorDisplayNameSnapshot: $"{user.FirstName} {user.LastName}"), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        // Re-issues the auth cookie against the now-current security stamp and
        // MustChangePassword=false, so RequirePasswordChangeMiddleware stops
        // gating this session on its very next request rather than only after
        // ActiveUserCookieValidator happens to re-check. Only after the commit,
        // so a cookie is never issued for a stamp that was rolled back.
        await signInManager.RefreshSignInAsync(user);

        return Ok();
    }

    private async Task<CurrentUserResponse> BuildCurrentUserResponseAsync(ApplicationUser user)
    {
        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;
        var permissions = RolePermissions.GetPermissions(role);

        return new CurrentUserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email ?? string.Empty,
            role,
            permissions,
            user.MustChangePassword);
    }

    /// <summary>
    /// <c>PasswordMismatch</c> is about the current password; every password-policy
    /// error (<c>PasswordTooShort</c>, <c>PasswordRequiresDigit</c>/<c>Lower</c>/
    /// <c>Upper</c>/<c>NonAlphanumeric</c>/<c>UniqueChars</c>) is about the new one.
    /// Anything else is not attributable to a field.
    /// </summary>
    internal static string ChangePasswordErrorField(string code) => code switch
    {
        nameof(IdentityErrorDescriber.PasswordMismatch) => nameof(ChangePasswordRequest.CurrentPassword),
        _ when code.StartsWith("Password", StringComparison.Ordinal) => nameof(ChangePasswordRequest.NewPassword),
        _ => string.Empty,
    };

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
