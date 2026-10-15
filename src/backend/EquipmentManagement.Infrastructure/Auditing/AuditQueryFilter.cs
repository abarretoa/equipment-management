namespace EquipmentManagement.Infrastructure.Auditing;

/// <summary>Filters supported by the read-only audit query API (docs/PROJECT_SPEC.md, section 15/25).</summary>
public sealed record AuditQueryFilter(
    Guid? ActorUserId,
    string? Action,
    string? EntityType,
    string? EntityId,
    DateTime? FromUtc,
    DateTime? ToUtc);
