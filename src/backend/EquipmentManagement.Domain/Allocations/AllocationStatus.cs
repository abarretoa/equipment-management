namespace EquipmentManagement.Domain.Allocations;

/// <summary>
/// Explicit lifecycle state, not derived from <see cref="ConsultantAllocation.EndDate"/>
/// being null/past (Phase 2 scope) — a
/// known-in-advance end date doesn't make an allocation over yet, and "over" is
/// always a deliberate action (<c>POST .../end</c>), never a side effect of the
/// calendar. See ADR-021 and ADR-067.
/// </summary>
public enum AllocationStatus
{
    Active,
    Ended,
}
