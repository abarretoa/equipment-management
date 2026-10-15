using EquipmentManagement.Domain.ResponsibilityTerms;

namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// One email-send attempt notifying the Consultant of an
/// <see cref="EquipmentDispatch"/> — mirrors
/// <c>ResponsibilityTerms.ResponsibilityTermDelivery</c>'s exact shape and
/// ordering discipline: inserted as <see cref="DeliveryStatus.Attempting"/>
/// and committed *before* the email is handed to the mail transport, updated
/// exactly once afterward to <see cref="DeliveryStatus.Sent"/> or
/// <see cref="DeliveryStatus.Failed"/>. The dispatch operation itself is
/// already durably persisted by the time this row is created — email failure
/// here never rolls back the dispatch, lifecycle, or custody.
/// </summary>
public sealed class EquipmentDispatchNotification
{
    public Guid Id { get; set; }

    public Guid EquipmentDispatchId { get; set; }

    public required string RecipientEmailSnapshot { get; set; }

    public DeliveryStatus Status { get; set; }

    public DateTime AttemptedAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public DeliveryFailureCode? FailureCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
