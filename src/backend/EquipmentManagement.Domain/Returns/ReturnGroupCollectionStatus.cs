namespace EquipmentManagement.Domain.Returns;

/// <summary>
/// How far the physical collection of an <see cref="EquipmentReturnGroup"/>
/// has progressed. Always derived from its returns
/// (<see cref="ReturnRules.DeriveGroupCollectionStatus"/>), never persisted.
/// </summary>
public enum ReturnGroupCollectionStatus
{
    /// <summary>No return in the group has been physically received yet.</summary>
    Awaiting,

    /// <summary>Some, but not all, of the returns have been physically received.</summary>
    Partial,

    /// <summary>Every return that was not cancelled has been physically received.</summary>
    Collected,

    /// <summary>Every return in the group was cancelled: there is nothing left to collect.</summary>
    Cancelled,
}
