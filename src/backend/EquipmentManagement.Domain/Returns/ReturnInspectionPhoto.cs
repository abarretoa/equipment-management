namespace EquipmentManagement.Domain.Returns;

/// <summary>
/// Metadata for one return-evidence photo attached to a <see cref="ReturnInspection"/>
/// (docs/PROJECT_SPEC.md, sections 12, 23; Phase 5 scope). Same
/// shape and rules as <see cref="Handovers.DeliveryInspectionPhoto"/> — a distinct
/// table, not a reuse of the delivery-photo relationship, since return evidence
/// must never be confused with (or overwrite) delivery evidence.
/// </summary>
public sealed class ReturnInspectionPhoto
{
    public Guid Id { get; set; }

    public Guid ReturnInspectionId { get; set; }

    public required string StorageKey { get; set; }

    public required string OriginalFileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public required string Sha256Hash { get; set; }

    public int SortOrder { get; set; }

    public DateTime UploadedAtUtc { get; set; }

    public Guid UploadedByUserId { get; set; }
}
