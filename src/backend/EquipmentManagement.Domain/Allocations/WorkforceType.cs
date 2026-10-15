namespace EquipmentManagement.Domain.Allocations;

/// <summary>
/// Whether an allocation is with the company operating this platform
/// (<see cref="Internal"/>) or with an external client (<see cref="External"/>).
/// Independent of <see cref="EngagementType"/> — a consultant can be any
/// combination of the two (Phase 2 scope). Persisted as text (see ApplicationDbContext), not the numeric value.
/// </summary>
public enum WorkforceType
{
    Internal,
    External,
}
