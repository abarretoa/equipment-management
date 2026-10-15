namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// The delivery-time inspection of exactly one <see cref="EquipmentHandoverItem"/>
/// (docs/PROJECT_SPEC.md, section 10: "inspeção inicial"). At most one per item
/// — see the unique index on <see cref="EquipmentHandoverItemId"/> — recorded
/// while the parent handover is still a draft, and revalidated (not re-created)
/// at finalization.
/// </summary>
public sealed class DeliveryInspection
{
    public Guid Id { get; set; }

    public Guid EquipmentHandoverItemId { get; set; }

    public EquipmentCondition Condition { get; set; }

    public string? Notes { get; set; }

    public Guid InspectedByUserId { get; set; }

    public required string InspectedByDisplayNameSnapshot { get; set; }

    public DateTime InspectedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
