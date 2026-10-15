namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// The equipment's known physical condition as of <see cref="CreatedAtUtc"/> —
/// exactly 3 photos (see <see cref="EquipmentPhotoSetPhoto"/>), never fewer or
/// more. Represents the CURRENT known condition, not a per-Handover artifact:
/// only <see cref="PhotoSetStatus.Current"/> once <see cref="Status"/> flips to
/// <see cref="PhotoSetStatus.Historical"/> is the row ever superseded, and a
/// <see cref="Historical"/> row is never mutated again — it remains permanent
/// evidence (mirrors <c>ResponsibilityTerm</c>'s Generated/Superseded shape and
/// concurrency discipline exactly). A Handover freezes a reference to whichever
/// set is Current at the moment it is finalized
/// (<c>Handovers.EquipmentHandoverItem.EquipmentPhotoSetId</c>) — that
/// reference never changes even after a later Return supersedes this row,
/// which is what keeps a previously-generated Responsibility Term's photos
/// permanently correct.
/// </summary>
public sealed class EquipmentPhotoSet
{
    public Guid Id { get; set; }

    public Guid EquipmentId { get; set; }

    public PhotoSetStatus Status { get; set; }

    public PhotoSetSource Source { get; set; }

    /// <summary>
    /// Set only when <see cref="Source"/> is <see cref="PhotoSetSource.Return"/>
    /// — null for <see cref="PhotoSetSource.InitialRegistration"/>. Enforced by
    /// both the only two writers (never a caller-supplied combination) and a
    /// database check constraint (see ApplicationDbContext) — an
    /// InitialRegistration set with a Return reference, or vice versa, can
    /// never exist.
    /// </summary>
    public Guid? EquipmentReturnId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public required string CreatedByDisplayNameSnapshot { get; set; }
}
