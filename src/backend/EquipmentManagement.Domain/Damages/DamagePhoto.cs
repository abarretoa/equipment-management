namespace EquipmentManagement.Domain.Damages;

/// <summary>
/// Metadata for one damage-evidence photo (docs/PROJECT_SPEC.md, section 13;
/// invariant #9; Phase 5 scope). Attached to the
/// <see cref="Returns.ReturnInspection"/>, not to <see cref="Damage"/> directly —
/// damage evidence must be uploadable while <see cref="Damage"/> doesn't exist
/// yet (it's only materialized at finalization, see that entity's remarks), the
/// same way <see cref="Handovers.DeliveryInspectionPhoto"/> attaches to the
/// inspection rather than to a not-yet-generated document. A finalized
/// <see cref="Damage"/>'s evidence is queried via its 1:1
/// <see cref="Damage.ReturnInspectionId"/> back-reference.
/// </summary>
public sealed class DamagePhoto
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
