namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// The operational (not audit-log) timeline vocabulary for one piece of
/// Equipment (Iteration 1). Every value is assembled
/// read-only from existing domain rows — <see cref="Handovers.EquipmentAssignment"/>,
/// <see cref="Handovers.EquipmentHandover"/> snapshots, <see cref="Returns.EquipmentReturn"/>/
/// <see cref="Returns.ReturnInspection"/>, <see cref="Damages.Damage"/>,
/// <see cref="Maintenance.MaintenanceRecord"/>, and <see cref="Equipment"/>
/// itself — never a new persisted row and never the Audit Log.
/// </summary>
public enum EquipmentHistoryEventType
{
    Registered,
    Assigned,
    Returned,
    ReturnInspectionCompleted,
    DamageReported,
    MaintenanceStarted,
    MaintenanceCompleted,
    RetirementRequested,
    RetirementRequestRejected,
    Retired,

    /// <summary>The equipment's first current photo set was established — either at creation or via the legacy backfill action.</summary>
    CurrentPhotosEstablished,

    /// <summary>The equipment's current photo set was replaced with a new one captured at Return physical receipt.</summary>
    CurrentPhotosUpdated,
}
