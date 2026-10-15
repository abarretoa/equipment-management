using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Maintenance;

namespace EquipmentManagement.UnitTests.Domain.Maintenance;

public class MaintenanceRulesTests
{
    [Fact]
    public void CanStart_TrueOnlyForDamaged()
    {
        Assert.True(MaintenanceRules.CanStart(EquipmentStatus.Damaged));
        Assert.False(MaintenanceRules.CanStart(EquipmentStatus.Available));
        Assert.False(MaintenanceRules.CanStart(EquipmentStatus.Assigned));
        Assert.False(MaintenanceRules.CanStart(EquipmentStatus.UnderMaintenance));
    }

    [Fact]
    public void CanComplete_TrueOnlyForActive()
    {
        Assert.True(MaintenanceRules.CanComplete(MaintenanceStatus.Active));
        Assert.False(MaintenanceRules.CanComplete(MaintenanceStatus.Completed));
    }

    [Fact]
    public void ResultingEquipmentStatus_RepairedGoesToAvailable()
    {
        Assert.Equal(EquipmentStatus.Available, MaintenanceRules.ResultingEquipmentStatus(MaintenanceOutcome.Repaired));
    }

    [Fact]
    public void ResultingEquipmentStatus_NotRepairableGoesBackToDamaged()
    {
        Assert.Equal(EquipmentStatus.Damaged, MaintenanceRules.ResultingEquipmentStatus(MaintenanceOutcome.NotRepairable));
    }
}
