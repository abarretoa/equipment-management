using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Auditing;

/// <summary>
/// Read-only access to the audit log (docs/PROJECT_SPEC.md, section 15/25). No
/// method here can mutate a row — the append-only guarantee lives structurally in
/// this type's shape, not just in a code comment.
/// </summary>
public sealed class AuditQueryService(ApplicationDbContext dbContext)
{
    public async Task<PagedResult<AuditLog>> QueryAsync(
        AuditQueryFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.AuditLogs.AsQueryable();

        if (filter.ActorUserId is { } actorUserId)
        {
            query = query.Where(a => a.ActorUserId == actorUserId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(a => a.Action == filter.Action);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            query = query.Where(a => a.EntityType == filter.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            query = query.Where(a => a.EntityId == filter.EntityId);
        }

        if (filter.FromUtc is { } fromUtc)
        {
            query = query.Where(a => a.OccurredAtUtc >= fromUtc);
        }

        if (filter.ToUtc is { } toUtc)
        {
            query = query.Where(a => a.OccurredAtUtc <= toUtc);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 200);

        var items = await query
            .OrderByDescending(a => a.OccurredAtUtc)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLog>(items, page1Based, boundedPageSize, totalCount);
    }

    public Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.AuditLogs.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
}
