namespace EquipmentManagement.Domain.ResponsibilityTerms;

/// <summary>
/// One immutable, historical PDF artifact generated for a finalized
/// <see cref="Handovers.EquipmentHandover"/> (docs/PROJECT_SPEC.md, section 11;
/// Phase 6 scope). A handover may have many terms over time (each
/// regeneration creates a new row, never overwriting an old one — see
/// <see cref="Infrastructure.ResponsibilityTerms.ResponsibilityTermService.GenerateAsync"/>),
/// but at most one may be <see cref="ResponsibilityTermStatus.Generated"/>
/// (current) at a time — enforced by a partial unique index, not just
/// application logic. Once inserted, no code path anywhere updates
/// <see cref="DocumentNumber"/>, <see cref="Version"/>, <see cref="StorageKey"/>,
/// <see cref="Sha256"/>, or <see cref="SizeBytes"/> — the only mutation ever
/// applied to an existing row is the one-way <see cref="Status"/> transition
/// <c>Generated -&gt; Superseded</c> when a later version is generated.
/// </summary>
public sealed class ResponsibilityTerm
{
    public Guid Id { get; set; }

    public Guid EquipmentHandoverId { get; set; }

    /// <summary>
    /// Stable, human-readable, globally unique identifier (e.g. "TR-2026-000123") —
    /// the actual legal identity of the document, independent of any filename
    /// (Phase 6 scope). Generated once from a database sequence,
    /// never recomputed — same pattern as <c>Equipment.AssetTag</c> (ADR-026).
    /// </summary>
    public required string DocumentNumber { get; set; }

    /// <summary>1, 2, 3... per <see cref="EquipmentHandoverId"/> — monotonic, allocated inside a Serializable transaction, never <c>MAX+1</c> without concurrency protection (see ADR-049).</summary>
    public int Version { get; set; }

    public ResponsibilityTermStatus Status { get; set; }

    /// <summary>
    /// Which version of the document template/legal wording produced this
    /// PDF (Phase 6 scope) — preserved forever so a future
    /// wording change never reinterprets an already-issued document.
    /// </summary>
    public int TemplateVersion { get; set; }

    public DateTime GeneratedAtUtc { get; set; }

    public Guid GeneratedByUserId { get; set; }

    /// <summary>Historical snapshot, not a live lookup — readable even if the generating user's name later changes or they become inactive.</summary>
    public required string GeneratedByDisplayNameSnapshot { get; set; }

    /// <summary>Server-generated, opaque — never derived from Consultant name/NIF/handover content (docs/PROJECT_SPEC.md, section 23).</summary>
    public required string StorageKey { get; set; }

    /// <summary>The human-friendly download filename (built from <see cref="DocumentNumber"/>/<see cref="Version"/>, never Consultant name/NIF) — see <c>ResponsibilityTermService</c>.</summary>
    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>SHA-256 of the exact stored PDF bytes — evidence a later download is byte-identical to what was generated, not an authorization mechanism (Phase 6 scope).</summary>
    public required string Sha256 { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// The Consultant data THIS version was rendered from, frozen with it and
    /// never changed afterwards (with <see cref="AllocationSnapshot"/> and this
    /// version's <see cref="ResponsibilityTermItem"/> rows). Each version owns
    /// its own copy: a later version, a dispatch cancellation or the
    /// Handover's delivery record never touches it. Null only for a legacy
    /// version whose data could not be recovered (<see cref="SnapshotUnavailable"/>).
    /// </summary>
    public Handovers.ConsultantSnapshot? ConsultantSnapshot { get; set; }

    /// <summary>The professional context this version was rendered from; null when the Consultant had none.</summary>
    public Handovers.AllocationSnapshot? AllocationSnapshot { get; set; }

    /// <summary>
    /// True only for a version generated before per-version snapshots existed
    /// whose exact data could not be proven (it was overwritten by a later
    /// version under the old shared model). Its PDF and SHA-256 remain the
    /// evidence; no snapshot is ever fabricated for it. Every new version has
    /// a complete snapshot (a check constraint enforces the Consultant part).
    /// </summary>
    public bool SnapshotUnavailable { get; set; }
}
