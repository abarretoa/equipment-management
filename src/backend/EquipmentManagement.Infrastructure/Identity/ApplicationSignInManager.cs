using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.Infrastructure.Identity;

/// <summary>
/// Overrides <see cref="CanSignInAsync"/> — the extension point ASP.NET Core
/// Identity provides for exactly this purpose — so a deactivated internal user
/// (<see cref="ApplicationUser.IsActive"/> false) is rejected at login with the
/// same <see cref="SignInResult.NotAllowed"/> outcome Identity already uses for
/// "account exists but may not sign in," rather than a bespoke check bolted onto
/// the login endpoint. AuthController still maps this to the same generic
/// "Invalid email or password" response as any other failure, so the
/// response never reveals whether an account exists.
/// </summary>
public sealed class ApplicationSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ApplicationUser user) =>
        user.IsActive && await base.CanSignInAsync(user);
}
