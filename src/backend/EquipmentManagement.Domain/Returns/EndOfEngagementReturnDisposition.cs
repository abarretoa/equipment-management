namespace EquipmentManagement.Domain.Returns;

/// <summary>
/// The outcome of <see cref="ReturnRules.ClassifyForEndOfEngagement"/> for one
/// piece of equipment in a Consultant's active custody. A transient decision,
/// never persisted: an <see cref="EquipmentReturn"/> started by the
/// end-of-engagement operation is an ordinary return, distinguishable only
/// through the audit trail.
/// </summary>
public enum EndOfEngagementReturnDisposition
{
    /// <summary>Assigned, with no return in progress: a new return is initiated.</summary>
    Initiate,

    /// <summary>A return is already pending physical receipt: it is preserved untouched.</summary>
    AlreadyPending,

    /// <summary>The equipment/return state is inconsistent with active custody: the engagement cannot be ended until it is corrected.</summary>
    RequiresAttention,
}
