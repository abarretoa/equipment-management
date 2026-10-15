using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.Domain.Maintenance;

/// <summary>
/// Pure business rules for the maintenance workflow (docs/PROJECT_SPEC.md,
/// section 13; Phase 5 scope). Framework- and database-free —
/// <c>Infrastructure.Maintenance.MaintenanceService</c> supplies the actual rows.
/// </summary>
public static class MaintenanceRules
{
    /// <summary>Only <c>Damaged</c> equipment may enter maintenance (docs/PROJECT_SPEC.md, section 9).</summary>
    public static bool CanStart(EquipmentStatus equipmentStatus) => equipmentStatus == EquipmentStatus.Damaged;

    /// <summary>Only an <c>Active</c> maintenance record can be completed.</summary>
    public static bool CanComplete(MaintenanceStatus status) => status == MaintenanceStatus.Active;

    /// <summary><c>Repaired</c> returns equipment to circulation; <c>NotRepairable</c> sends it back to <c>Damaged</c> for an explicit, separate Administration retirement decision (Phase 5 scope).</summary>
    public static EquipmentStatus ResultingEquipmentStatus(MaintenanceOutcome outcome) => outcome switch
    {
        MaintenanceOutcome.Repaired => EquipmentStatus.Available,
        MaintenanceOutcome.NotRepairable => EquipmentStatus.Damaged,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
