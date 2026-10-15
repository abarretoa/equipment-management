using EquipmentManagement.Domain.Users;

namespace EquipmentManagement.UnitTests.Domain.Users;

public class UserProfileRulesTests
{
    [Theory]
    [InlineData(Roles.Administration, true)]
    [InlineData(Roles.HR, true)]
    [InlineData("CEO", false)]
    [InlineData("SuperAdmin", false)]
    [InlineData("", false)]
    public void IsSupportedRole_OnlyAcceptsTheTwoFixedRoles(string role, bool expected)
    {
        Assert.Equal(expected, UserProfileRules.IsSupportedRole(role));
    }

    [Fact]
    public void RequiresAddress_TrueForHr()
    {
        Assert.True(UserProfileRules.RequiresAddress(Roles.HR));
    }

    [Fact]
    public void RequiresAddress_FalseForAdministration()
    {
        Assert.False(UserProfileRules.RequiresAddress(Roles.Administration));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("Rua Real 1, Lisboa", false)]
    public void IsAddressMissing_DetectsBlankValues(string? address, bool expected)
    {
        Assert.Equal(expected, UserProfileRules.IsAddressMissing(address));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("+351912345678", "+351912345678")]
    [InlineData("+351 912 345 678", "+351 912 345 678")]
    public void NormalizeOptionalPhone_MapsBlankToNull_AndKeepsAnyOtherValueAsEntered(string? phone, string? expected)
    {
        Assert.Equal(expected, UserProfileRules.NormalizeOptionalPhone(phone));
    }

    [Fact]
    public void Limits_MatchTheUserColumns()
    {
        Assert.Equal(200, UserProfileRules.MaxNameLength);
        Assert.Equal(500, UserProfileRules.MaxAddressLength);
    }
}
