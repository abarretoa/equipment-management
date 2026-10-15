using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.UnitTests.Domain.Equipments;

public class EquipmentLifecycleRulesTests
{
    [Fact]
    public void CanRetire_TrueWhenAvailable()
    {
        Assert.True(EquipmentLifecycleRules.CanRetire(EquipmentStatus.Available));
    }

    [Fact]
    public void CanRetire_TrueWhenDamaged()
    {
        Assert.True(EquipmentLifecycleRules.CanRetire(EquipmentStatus.Damaged));
    }

    [Theory]
    [InlineData(EquipmentStatus.InTransit)]
    [InlineData(EquipmentStatus.Assigned)]
    [InlineData(EquipmentStatus.ReturnPending)]
    [InlineData(EquipmentStatus.UnderInspection)]
    [InlineData(EquipmentStatus.UnderMaintenance)]
    [InlineData(EquipmentStatus.Retired)]
    public void CanRetire_FalseForEveryOtherStatus(EquipmentStatus status)
    {
        Assert.False(EquipmentLifecycleRules.CanRetire(status));
    }

    [Fact]
    public void CanAssign_TrueWhenAvailable()
    {
        Assert.True(EquipmentLifecycleRules.CanAssign(EquipmentStatus.Available));
    }

    [Theory]
    [InlineData(EquipmentStatus.InTransit)]
    [InlineData(EquipmentStatus.Assigned)]
    [InlineData(EquipmentStatus.ReturnPending)]
    [InlineData(EquipmentStatus.UnderInspection)]
    [InlineData(EquipmentStatus.Damaged)]
    [InlineData(EquipmentStatus.UnderMaintenance)]
    [InlineData(EquipmentStatus.Retired)]
    public void CanAssign_FalseForEveryOtherStatus(EquipmentStatus status)
    {
        Assert.False(EquipmentLifecycleRules.CanAssign(status));
    }

    [Fact]
    public void CanDispatch_TrueOnlyWhenAvailable()
    {
        Assert.Equal(
            [EquipmentStatus.Available],
            Enum.GetValues<EquipmentStatus>().Where(EquipmentLifecycleRules.CanDispatch));
    }

    [Fact]
    public void DeliveryConfirmation_CarrierRequiresInTransit_InPersonRequiresAvailable()
    {
        Assert.Equal(EquipmentStatus.InTransit, EquipmentLifecycleRules.RequiredStatusForDeliveryConfirmation(isCarrier: true));
        Assert.Equal(EquipmentStatus.Available, EquipmentLifecycleRules.RequiredStatusForDeliveryConfirmation(isCarrier: false));
    }

    [Fact]
    public void CanCancelDispatch_TrueOnlyWhenInTransit()
    {
        Assert.Equal(
            [EquipmentStatus.InTransit],
            Enum.GetValues<EquipmentStatus>().Where(EquipmentLifecycleRules.CanCancelDispatch));
    }
}
