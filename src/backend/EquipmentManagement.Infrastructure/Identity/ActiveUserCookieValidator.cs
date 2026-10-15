using System.Security.Claims;
using EquipmentManagement.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.Infrastructure.Identity;

/// <summary>
/// Re-validates every cookie-authenticated request against the current database
/// state, so deactivating a user takes effect immediately instead of only at their
/// next login (Phase 1 scope).
/// </summary>
/// <remarks>
/// Phase 0 wired Identity via <c>AddIdentityCore</c>, not the full
/// <c>AddIdentity</c>, so the framework's own <c>SecurityStampValidator</c> —
/// which would normally do exactly this — is never registered. Re-implementing
/// the check here (security stamp match + <see cref="ApplicationUser.IsActive"/>)
/// on every request is a deliberate simplicity choice over adding a validation
/// interval/cache: this is a small internal application, so one indexed
/// primary-key lookup per request is a fine cost for immediate revocation. See the
/// ADR on inactive-session handling. While this handler already has the user
/// loaded, it also stashes <see cref="ApplicationUser.MustChangePassword"/> into
/// <see cref="HttpContext.Items"/> under <see cref="MustChangePasswordItemKey"/>,
/// which <c>Api.Middleware.RequirePasswordChangeMiddleware</c> reads — avoiding a
/// second database round trip for that check on the same request.
/// </remarks>
public static class ActiveUserCookieValidator
{
    public const string MustChangePasswordItemKey = "MustChangePassword";

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdValue is null || !Guid.TryParse(userIdValue, out var userId))
        {
            context.RejectPrincipal();
            return;
        }

        // Permission claims are frozen into the cookie at sign-in. A deployment
        // that changes RolePermissions does not touch any security stamp, so the
        // cookie must also prove it was issued under the current map (ADR-078).
        // A missing claim (a cookie issued before this check existed) fails
        // closed. Checked before the user lookup: no extra database access.
        var mapInCookie = context.Principal?.FindFirstValue(ApplicationClaimsPrincipalFactory.PermissionMapClaimType);
        if (!string.Equals(mapInCookie, RolePermissions.Fingerprint, StringComparison.Ordinal))
        {
            await RejectAndSignOutAsync(context);
            return;
        }

        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null || !user.IsActive)
        {
            await RejectAndSignOutAsync(context);
            return;
        }

        var identityOptions = context.HttpContext.RequestServices.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var stampInCookie = context.Principal?.FindFirstValue(identityOptions.ClaimsIdentity.SecurityStampClaimType);
        var currentStamp = await userManager.GetSecurityStampAsync(user);

        if (!string.Equals(stampInCookie, currentStamp, StringComparison.Ordinal))
        {
            await RejectAndSignOutAsync(context);
            return;
        }

        context.HttpContext.Items[MustChangePasswordItemKey] = user.MustChangePassword;
    }

    private static async Task RejectAndSignOutAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
