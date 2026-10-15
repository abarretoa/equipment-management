namespace EquipmentManagement.Domain.Handovers;

/// <summary>
/// Metadata for one evidence photo attached to a <see cref="DeliveryInspection"/>
/// (docs/PROJECT_SPEC.md, sections 10, 23). The binary itself is never stored
/// here — only in the private object storage identified by
/// <see cref="StorageKey"/> (docs/PROJECT_SPEC.md, section 23: "o banco
/// armazena apenas metadados"). <see cref="StorageKey"/> is always
/// server-generated; <see cref="OriginalFileName"/> is preserved only for
/// display and is never used to build a path.
/// </summary>
public sealed class DeliveryInspectionPhoto
{
    public Guid Id { get; set; }

    public Guid DeliveryInspectionId { get; set; }

    public required string StorageKey { get; set; }

    public required string OriginalFileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public required string Sha256Hash { get; set; }

    public int SortOrder { get; set; }

    public DateTime UploadedAtUtc { get; set; }

    public Guid UploadedByUserId { get; set; }
}
