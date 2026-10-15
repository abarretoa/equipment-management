namespace EquipmentManagement.Domain.Maintenance;

/// <summary>Set only when a <see cref="MaintenanceRecord"/> is completed (Phase 5 scope).</summary>
public enum MaintenanceOutcome
{
    Repaired,
    NotRepairable,
}
