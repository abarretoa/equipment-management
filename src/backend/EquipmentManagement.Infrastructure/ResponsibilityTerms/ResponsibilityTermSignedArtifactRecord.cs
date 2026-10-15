namespace EquipmentManagement.Infrastructure.ResponsibilityTerms;

/// <summary>Never <c>StorageKey</c> — same discipline as <c>ResponsibilityTermRecord</c> (Phase 6 scope; Phase 7 scope).</summary>
public sealed record ResponsibilityTermSignedArtifactRecord(
    Guid Id,
    Guid ResponsibilityTermId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DateOnly? SignedOn,
    DateTime UploadedAtUtc,
    string UploadedByDisplayNameSnapshot);
