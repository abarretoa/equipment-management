namespace EquipmentManagement.Domain.Returns;

/// <summary>
/// One return-initiation event for one Consultant: the returns started
/// together, either by ending the Consultant's engagement or by HR starting a
/// manual return for one or more items (Iteration 4). It exists so
/// the Returns area can show that event as one collection instead of N
/// unrelated rows — nothing more.
///
/// It is deliberately not a second return workflow. Each
/// <see cref="EquipmentReturn"/> in it stays an independent return with its
/// own receipt, inspection, damage and audit trail, and equipment lifecycle
/// and custody are untouched by grouping. The group therefore stores no
/// status and no expected date: its collection status is derived from its
/// returns (<see cref="ReturnRules.DeriveGroupCollectionStatus"/>), and its
/// expected date from the fields that already hold it
/// (<see cref="Allocations.ConsultantAllocation.ExpectedCollectionFromDate"/>
/// for an end of engagement, <see cref="EquipmentReturn.ExpectedReturnDate"/>
/// otherwise).
///
/// Membership is fixed at creation: a return is never moved into or out of a
/// group afterwards, and a group is never deleted. Returns created before
/// grouping existed have no group and remain valid.
/// </summary>
public sealed class EquipmentReturnGroup
{
    public Guid Id { get; set; }

    public Guid ConsultantId { get; set; }

    /// <summary>
    /// The allocation whose end-of-engagement ending created this group, or
    /// null for a manual return. This real relationship is what distinguishes
    /// the two kinds; there is no separate origin flag.
    /// </summary>
    public Guid? ConsultantAllocationId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public required string CreatedByDisplayNameSnapshot { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
