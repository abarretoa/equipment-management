namespace EquipmentManagement.Domain.Equipments;

/// <summary>
/// One of the exactly 3 photos in an <see cref="EquipmentPhotoSet"/> — same
/// shape as <c>Handovers.DeliveryInspectionPhoto</c>/<c>Returns.ReturnInspectionPhoto</c>.
/// The binary itself is never stored here, only in private object storage
/// identified by <see cref="StorageKey"/>. Exactly 3 rows per set is enforced
/// in application code (all 3 are always written together, in one service
/// call), not a database constraint. <see cref="SortOrder"/>'s own shape —
/// values 1-3 and uniqueness within a set — IS additionally backed by a DB
/// `CHECK` constraint and a unique index (see
/// <c>ApplicationDbContext.OnModelCreating</c>), since that's cheap,
/// unambiguous, and independent of the (still application-only) count check.
/// </summary>
public sealed class EquipmentPhotoSetPhoto
{
    public Guid Id { get; set; }

    public Guid EquipmentPhotoSetId { get; set; }

    public required string StorageKey { get; set; }

    public required string OriginalFileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public required string Sha256Hash { get; set; }

    public int SortOrder { get; set; }

    public DateTime UploadedAtUtc { get; set; }

    public Guid UploadedByUserId { get; set; }
}
