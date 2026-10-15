using EquipmentManagement.Domain.ResponsibilityTerms;

namespace EquipmentManagement.Infrastructure.ResponsibilityTerms;

/// <summary>Never includes <c>StorageKey</c> — that is an internal implementation detail, not part of any API-facing projection (Phase 6 scope).</summary>
public sealed record ResponsibilityTermRecord(
    Guid Id,
    Guid EquipmentHandoverId,
    string DocumentNumber,
    int Version,
    ResponsibilityTermStatus Status,
    int TemplateVersion,
    DateTime GeneratedAtUtc,
    string GeneratedByDisplayNameSnapshot,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
