using System.Security.Claims;
using EquipmentManagement.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.Infrastructure.Identity;

/// <summary>
/// Identity's standard user+role claims factory, plus one claim: the
/// fingerprint of the <see cref="RolePermissions"/> map the permission claims
/// were issued under (ADR-078). Every sign-in and cookie refresh goes through
/// here, and <see cref="ActiveUserCookieValidator"/> rejects any cookie whose
/// fingerprint is missing or no longer current.
/// </summary>
public sealed class ApplicationClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(userManager, roleManager, options)
{
    public const string PermissionMapClaimType = "equipment-management:permission-map";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(PermissionMapClaimType, RolePermissions.Fingerprint));
        return identity;
    }
}
