namespace EquipmentManagement.Domain.Maintenance;

/// <summary>The minimal maintenance lifecycle (docs/PROJECT_SPEC.md, section 13; Phase 5 scope) — no vendor/cost/quote workflow.</summary>
public enum MaintenanceStatus
{
    Active,
    Completed,
}
