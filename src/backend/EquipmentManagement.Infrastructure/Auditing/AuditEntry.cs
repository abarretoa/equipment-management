namespace EquipmentManagement.Infrastructure.Auditing;

/// <summary>
/// Input to <see cref="AuditService.RecordAsync"/>. Callers pass only explicit,
/// pre-redacted safe fields for <paramref name="OldValues"/>/<paramref
/// name="NewValues"/>/<paramref name="Metadata"/> — never a raw persistence
/// entity (docs/PROJECT_SPEC.md, section 15; Phase 1 scope).
/// </summary>
public sealed record AuditEntry(
    string Action,
    string EntityType,
    string? EntityId,
    Guid? ActorUserId,
    string? ActorDisplayNameSnapshot,
    object? OldValues = null,
    object? NewValues = null,
    object? Metadata = null);
