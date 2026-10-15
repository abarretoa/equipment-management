namespace EquipmentManagement.Api.Contracts.Audit;

/// <summary>
/// The wire shape of an audit event (docs/PROJECT_SPEC.md, section 15). Every
/// field here was already safe-field-redacted when the event was recorded (see
/// Infrastructure.Auditing.AuditService callers) — this DTO does no further
/// filtering, it's a direct projection of the append-only AuditLog row.
/// </summary>
public sealed record AuditLogResponse(
    Guid Id,
    Guid? ActorUserId,
    string? ActorDisplayNameSnapshot,
    string Action,
    string EntityType,
    string? EntityId,
    DateTime OccurredAtUtc,
    string? CorrelationId,
    string? IpAddress,
    string? OldValuesJson,
    string? NewValuesJson,
    string? MetadataJson);
