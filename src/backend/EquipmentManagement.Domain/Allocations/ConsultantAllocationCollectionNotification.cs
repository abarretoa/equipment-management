using EquipmentManagement.Domain.ResponsibilityTerms;

namespace EquipmentManagement.Domain.Allocations;

/// <summary>
/// One email-send attempt notifying the Consultant to expect equipment
/// collection, sent after an end-of-engagement <see cref="ConsultantAllocation"/>
/// ending with active equipment custody — mirrors
/// <c>ResponsibilityTerms.ResponsibilityTermDelivery</c>'s exact shape and
/// ordering discipline. The allocation end is already durably persisted by
/// the time this row is created — email failure here never rolls back the
/// allocation end or touches equipment custody/lifecycle.
/// </summary>
public sealed class ConsultantAllocationCollectionNotification
{
    public Guid Id { get; set; }

    public Guid ConsultantAllocationId { get; set; }

    public required string RecipientEmailSnapshot { get; set; }

    public DeliveryStatus Status { get; set; }

    public DateTime AttemptedAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public DeliveryFailureCode? FailureCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
