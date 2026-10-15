using EquipmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.Middleware;

/// <summary>
/// Enforces "a temporary password must be changed before anything else" server
/// side (Phase 1 scope) — not just
/// as a frontend redirect the API would happily bypass. Runs after
/// authentication: <see cref="ActiveUserCookieValidator"/> already stashed
/// whether the signed-in user must change their password into
/// <see cref="HttpContext.Items"/> while validating the cookie, so this costs no
/// extra database call.
/// </summary>
public sealed class RequirePasswordChangeMiddleware(RequestDelegate next)
{
    /// <summary>Endpoints an authenticated-but-must-change-password user may still reach.</summary>
    private static readonly string[] AllowedPaths =
    [
        "/api/auth/change-password",
        "/api/auth/logout",
        "/api/auth/me",
        "/api/antiforgery/token",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var mustChangePassword = context.Items.TryGetValue(ActiveUserCookieValidator.MustChangePasswordItemKey, out var value) &&
            value is true;

        var path = context.Request.Path.Value ?? string.Empty;
        var isAllowed = AllowedPaths.Any(allowed => path.Equals(allowed, StringComparison.OrdinalIgnoreCase)) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase);

        if (mustChangePassword && !isAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "É necessário alterar a palavra-passe.",
                Detail = "Esta conta tem uma palavra-passe temporária que tem de ser alterada antes de continuar.",
            });
            return;
        }

        await next(context);
    }
}
