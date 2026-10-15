namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// Outcome of <see cref="HandoverRules.ResolveConsultantEligibility"/>. Three
/// states rather than a bool so that inconsistent data (more than one Active
/// allocation, which the one-Active-allocation invariant makes impossible on
/// the normal product path) is surfaced as its own error instead of being
/// silently resolved by picking one allocation.
/// </summary>
public enum ConsultantHandoverEligibility
{
    /// <summary>No Active allocation: the Consultant cannot receive a new delivery.</summary>
    NoActiveAllocation,

    /// <summary>Exactly one Active allocation: eligible, and that allocation is the delivery's professional context.</summary>
    Eligible,

    /// <summary>More than one Active allocation: invalid data. Never guess which one applies.</summary>
    MultipleActiveAllocations,
}
