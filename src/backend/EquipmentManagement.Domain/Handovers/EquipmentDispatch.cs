namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// One "Registar envio" attempt: HR physically shipped a Draft handover's
/// equipment to the Consultant. Distinct from and prior to
/// <see cref="EquipmentHandover.FinalizedAtUtc"/>: finalization is the sole
/// place custody (<c>EquipmentAssignment</c>) is created, so a dispatch never
/// creates custody; it moves the shipped equipment to <c>InTransit</c> and
/// carries the delivery estimate communicated to the Consultant.
/// <para>
/// A dispatch is <em>active</em> until cancelled ("Anular envio", before
/// delivery is confirmed). At most one active dispatch exists per handover
/// (partial unique index), and an active one is what locks the handover's
/// composition (see <c>HandoverRules.CanModifyComposition</c>). A cancelled
/// attempt is never deleted or rewritten: its row keeps who dispatched it and
/// when, and gains who cancelled it, when and why. A later dispatch of the
/// same handover is a new row.
/// </para>
/// </summary>
public sealed class EquipmentDispatch
{
    /// <summary>Upper bound for <see cref="CancellationReason"/>, matching the Return cancellation reason.</summary>
    public const int MaxCancellationReasonLength = 500;

    public Guid Id { get; set; }

    public Guid EquipmentHandoverId { get; set; }

    public DateOnly ExpectedDeliveryFromDate { get; set; }

    /// <summary>Null means a single expected date (<see cref="ExpectedDeliveryFromDate"/> only), not a range.</summary>
    public DateOnly? ExpectedDeliveryToDate { get; set; }

    public DateTime DispatchedAtUtc { get; set; }

    public Guid DispatchedByUserId { get; set; }

    public required string DispatchedByDisplayNameSnapshot { get; set; }

    /// <summary>Set once, by "Anular envio"; null while the dispatch is active. The four cancellation fields are all null or all set (check constraint).</summary>
    public DateTime? CancelledAtUtc { get; set; }

    public Guid? CancelledByUserId { get; set; }

    public string? CancelledByDisplayNameSnapshot { get; set; }

    /// <summary>The mandatory, trimmed reason given for the cancellation.</summary>
    public string? CancellationReason { get; set; }
}
