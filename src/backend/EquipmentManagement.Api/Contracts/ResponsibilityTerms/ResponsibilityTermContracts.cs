using EquipmentManagement.Domain.ResponsibilityTerms;

namespace EquipmentManagement.Api.Contracts.ResponsibilityTerms;

/// <summary>
/// Never <c>StorageKey</c> (Phase 6 scope) — every field here
/// is server-derived; the generation request itself takes no body at all
/// (Phase 6 scope: document number, version, status, template
/// version, hash and consultant/equipment data are never caller-supplied).
/// <see cref="ExecutionStatus"/>/<see cref="Signature"/> describe the Phase 7
/// delivery/signature workflow — deliberately separate from
/// <see cref="Status"/>, which continues to describe only the document-
/// version lifecycle (Phase 7 scope).
/// </summary>
public sealed record ResponsibilityTermResponse(
    Guid Id,
    Guid EquipmentHandoverId,
    string DocumentNumber,
    int Version,
    ResponsibilityTermStatus Status,
    int TemplateVersion,
    DateTime GeneratedAtUtc,
    string GeneratedByDisplayNameSnapshot,
    string FileName,
    long SizeBytes,
    string Sha256,
    DateTime CreatedAtUtc,
    ResponsibilityTermExecutionStatus ExecutionStatus,
    DateTime? LastSentAtUtc,
    int SendAttemptCount,
    ResponsibilityTermSignatureResponse? Signature);

/// <summary>Never <c>StorageKey</c> — same discipline as the original PDF (Phase 7 scope).</summary>
public sealed record ResponsibilityTermSignatureResponse(
    DateOnly? SignedOn, DateTime UploadedAtUtc, long SizeBytes, string Sha256, string UploadedByDisplayNameSnapshot);

/// <summary>Optional recipient override (Phase 7 scope) — omit to use the historical Consultant snapshot email.</summary>
public sealed record SendResponsibilityTermRequest(string? RecipientEmail);

/// <summary><see cref="RecipientEmailSnapshot"/> is only ever returned on this term-scoped, authorized view — never a broad cross-term list (Phase 7 scope).</summary>
public sealed record ResponsibilityTermDeliveryResponse(
    Guid Id,
    string RecipientEmailSnapshot,
    bool RecipientOverridden,
    DeliveryStatus Status,
    DateTime AttemptedAtUtc,
    DateTime? SentAtUtc,
    DeliveryFailureCode? FailureCode,
    string SentByDisplayNameSnapshot,
    DateTime CreatedAtUtc);
