namespace EquipmentManagement.Domain.ResponsibilityTerms;

/// <summary>
/// The externally-signed PDF an authorized internal user (HR/Administration)
/// registered as the returned artifact for exactly one immutable
/// <see cref="ResponsibilityTerm"/> version (Phase 7 scope,
/// 20-29). Stored separately from — never overwriting — the original generated
/// PDF identified by <see cref="ResponsibilityTerm.StorageKey"/>; both remain
/// permanently, independently downloadable.
///
/// At most one row may exist per <see cref="ResponsibilityTermId"/> (a unique
/// index enforces this at the database level, Phase 7 scope) — an
/// incorrect upload has no replace path in this phase (the
/// conservative approach); correcting one requires a future,
/// explicit administrative workflow, not a silent overwrite.
///
/// Recording this row means only: "an authorized internal user registered this
/// PDF as the signed returned artifact for this exact document version." It
/// does NOT mean the system cryptographically verified any signature or the
/// signer's identity (Phase 7 scope) — that guarantee can only
/// come from a future digital-signature-provider integration, explicitly out
/// of scope here.
/// </summary>
public sealed class ResponsibilityTermSignedArtifact
{
    public Guid Id { get; set; }

    public Guid ResponsibilityTermId { get; set; }

    /// <summary>Server-generated, opaque — never derived from Consultant name/NIF/email (docs/PROJECT_SPEC.md, section 23).</summary>
    public required string StorageKey { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>SHA-256 of the exact stored signed-PDF bytes — evidence only, never an authorization mechanism (same discipline as <see cref="ResponsibilityTerm.Sha256"/>).</summary>
    public required string Sha256 { get; set; }

    /// <summary>
    /// Optionally entered by HR from the date printed/handwritten on the
    /// signed document itself (Phase 7 scope) — the server has no
    /// way to know the actual signing moment, so this is never inferred from
    /// <see cref="UploadedAtUtc"/>.
    /// </summary>
    public DateOnly? SignedOn { get; set; }

    public DateTime UploadedAtUtc { get; set; }

    public Guid UploadedByUserId { get; set; }

    public required string UploadedByDisplayNameSnapshot { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
