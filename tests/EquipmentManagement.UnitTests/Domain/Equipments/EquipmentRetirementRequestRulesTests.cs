using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.UnitTests.Domain.Equipments;

public class EquipmentRetirementRequestRulesTests
{
    [Fact]
    public void CanRequest_TrueWhenAvailable()
    {
        Assert.True(EquipmentRetirementRequestRules.CanRequest(EquipmentStatus.Available));
    }

    [Fact]
    public void CanRequest_TrueWhenDamaged()
    {
        Assert.True(EquipmentRetirementRequestRules.CanRequest(EquipmentStatus.Damaged));
    }

    [Theory]
    [InlineData(EquipmentStatus.Assigned)]
    [InlineData(EquipmentStatus.ReturnPending)]
    [InlineData(EquipmentStatus.UnderInspection)]
    [InlineData(EquipmentStatus.UnderMaintenance)]
    [InlineData(EquipmentStatus.Retired)]
    public void CanRequest_FalseForEveryOtherStatus(EquipmentStatus status)
    {
        Assert.False(EquipmentRetirementRequestRules.CanRequest(status));
    }

    [Fact]
    public void CanDecide_TrueOnlyWhenPending()
    {
        Assert.True(EquipmentRetirementRequestRules.CanDecide(EquipmentRetirementRequestStatus.Pending));
        Assert.False(EquipmentRetirementRequestRules.CanDecide(EquipmentRetirementRequestStatus.Approved));
        Assert.False(EquipmentRetirementRequestRules.CanDecide(EquipmentRetirementRequestStatus.Rejected));
    }
}
