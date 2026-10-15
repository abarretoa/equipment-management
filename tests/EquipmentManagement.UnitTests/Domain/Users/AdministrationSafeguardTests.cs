using EquipmentManagement.Domain.Users;

namespace EquipmentManagement.UnitTests.Domain.Users;

public class AdministrationSafeguardTests
{
    [Fact]
    public void CannotRemoveAccess_WhenNoOtherActiveAdministratorExists()
    {
        Assert.False(AdministrationSafeguard.CanRemoveAdministrationAccess(otherActiveAdministratorCount: 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void CanRemoveAccess_WhenAtLeastOneOtherActiveAdministratorExists(int otherCount)
    {
        Assert.True(AdministrationSafeguard.CanRemoveAdministrationAccess(otherCount));
    }
}
