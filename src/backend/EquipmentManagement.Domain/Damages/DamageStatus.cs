namespace EquipmentManagement.Domain.Damages;

/// <summary>
/// The minimal damage lifecycle (docs/PROJECT_SPEC.md, section 13; Phase 5 scope) — deliberately no <c>UNDER_REPAIR</c>/<c>AwaitingQuote</c>/
/// vendor-assessment states; <see cref="Maintenance.MaintenanceRecord"/> owns the
/// operational repair lifecycle separately.
/// </summary>
public enum DamageStatus
{
    Open,
    Resolved,
}
