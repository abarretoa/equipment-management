namespace EquipmentManagement.UnitTests.Infrastructure.ClientProfiles;

/// <summary>The neutral "default" demonstration profile (clients/default/, docs/WHITE_LABEL.md).</summary>
public sealed class DefaultClientProfileTests
{
    [Fact]
    public void DefaultProfile_IsANeutralDemonstration_WithANoticeOnEveryTerm()
    {
        var profile = TestClientProfiles.LoadFromRepository("default");

        Assert.Equal("default", profile.Identity.Id);
        Assert.Equal("Equipment Management", profile.Identity.ProductName);
        Assert.Equal("Acme Consulting", profile.Identity.OrganizationName);
        Assert.True(profile.Identity.IsDemonstration);
        Assert.Null(profile.Identity.MobileCarrierName);
        Assert.False(string.IsNullOrWhiteSpace(profile.ResponsibilityTerm.DemonstrationNotice));
        Assert.Null(profile.ResponsibilityTerm.Logo);
    }
}
