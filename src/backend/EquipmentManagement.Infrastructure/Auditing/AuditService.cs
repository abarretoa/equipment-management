using System.Text.Json;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Infrastructure.Observability;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace EquipmentManagement.Infrastructure.Auditing;

/// <summary>
/// Appends audit events (docs/PROJECT_SPEC.md, section 15). Nothing in this
/// codebase updates or deletes a row after <see cref="RecordAsync"/> inserts it —
/// there is no update/delete method here by design (Phase 1 scope:
/// "append-only from application perspective").
/// </summary>
public sealed class AuditService(
    ApplicationDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext;

        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = entry.ActorUserId,
            ActorDisplayNameSnapshot = entry.ActorDisplayNameSnapshot,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            // Already validated by the middleware; re-checked so no other code
            // path can ever put an unvalidated value into this bounded column.
            CorrelationId = httpContext?.Items[CorrelationId.HttpContextItemKey] is string id && CorrelationId.IsValid(id) ? id : null,
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
            OldValuesJson = Serialize(entry.OldValues),
            NewValuesJson = Serialize(entry.NewValues),
            MetadataJson = Serialize(entry.Metadata),
        };

        dbContext.AuditLogs.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, SerializerOptions);
}
