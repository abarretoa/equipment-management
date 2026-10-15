using System.Security.Claims;
using EquipmentManagement.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace EquipmentManagement.UnitTests.Infrastructure.Authorization;

public class PermissionAuthorizationHandlerTests
{
    private const string RequiredPermission = "Equipment.Read";

    [Fact]
    public async Task HandleAsync_Succeeds_WhenUserHasMatchingPermissionClaim()
    {
        var context = CreateContext(new Claim(PermissionAuthorizationHandler.PermissionClaimType, RequiredPermission));

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Fails_WhenUserHasOnlyADifferentPermissionClaim()
    {
        var context = CreateContext(new Claim(PermissionAuthorizationHandler.PermissionClaimType, "Consultant.Read"));

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Fails_WhenUserHasNoClaims()
    {
        var context = CreateContext();

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private static AuthorizationHandlerContext CreateContext(params Claim[] claims)
    {
        var requirement = new PermissionRequirement(RequiredPermission);
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        return new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(identity), resource: null);
    }
}
