namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// Mirrors <c>ResponsibilityTermStatus</c>'s exact Generated/Superseded shape:
/// at most one <see cref="Equipments.EquipmentPhotoSet"/> per equipment may be
/// <see cref="Current"/> at a time (a partial unique index backs this),
/// enforced by the same "reload inside a Serializable transaction, flip the
/// old row, insert the new one" pattern as
/// <c>ResponsibilityTermService.GenerateAsync</c>. A <see cref="Historical"/>
/// set is never mutated again — it remains permanent evidence.
/// </summary>
public enum PhotoSetStatus
{
    Current,
    Historical,
}
