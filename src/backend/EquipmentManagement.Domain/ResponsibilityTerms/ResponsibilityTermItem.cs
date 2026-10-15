using EquipmentManagement.Domain.Handovers;

namespace EquipmentManagement.Domain.ResponsibilityTerms;

/// <summary>
/// One equipment item exactly as a <see cref="ResponsibilityTerm"/> version
/// rendered it: its frozen <see cref="EquipmentSnapshot"/> and the exact
/// <see cref="EquipmentPhotoSetId"/> whose photos the PDF shows. Owned by
/// that version alone and written once, in the same transaction as the
/// version itself; a later version gets its own rows and never reuses,
/// overwrites or detaches these. Photo sets are permanent evidence, so the
/// link is protected by a Restrict foreign key.
/// </summary>
public sealed class ResponsibilityTermItem
{
    public Guid Id { get; set; }

    public Guid ResponsibilityTermId { get; set; }

    /// <summary>The equipment this row describes (never deleted; Restrict).</summary>
    public Guid EquipmentId { get; set; }

    /// <summary>Position of the item in the document, from 0.</summary>
    public int SortOrder { get; set; }

    public required EquipmentSnapshot EquipmentSnapshot { get; set; }

    /// <summary>The exact photo set rendered for this item; null for kinds that need no photos.</summary>
    public Guid? EquipmentPhotoSetId { get; set; }
}
