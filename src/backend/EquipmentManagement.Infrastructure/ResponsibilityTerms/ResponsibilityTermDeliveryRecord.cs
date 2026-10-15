using EquipmentManagement.Domain.ResponsibilityTerms;

namespace EquipmentManagement.Infrastructure.ResponsibilityTerms;

/// <summary>One send attempt, projected for API responses. <see cref="RecipientEmailSnapshot"/> is only ever exposed on an already-authorized, term-scoped view (Phase 7 scope) — never a broad/cross-term list.</summary>
public sealed record ResponsibilityTermDeliveryRecord(
    Guid Id,
    Guid ResponsibilityTermId,
    string RecipientEmailSnapshot,
    bool RecipientOverridden,
    DeliveryStatus Status,
    DateTime AttemptedAtUtc,
    DateTime? SentAtUtc,
    DeliveryFailureCode? FailureCode,
    string SentByDisplayNameSnapshot,
    DateTime CreatedAtUtc);
