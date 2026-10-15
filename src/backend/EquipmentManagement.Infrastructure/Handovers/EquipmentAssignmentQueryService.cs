using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Handovers;

/// <summary>
/// Read-only queries over <see cref="Domain.Handovers.EquipmentAssignment"/> —
/// "who currently has this equipment" / "what does this consultant currently
/// hold" (docs/PROJECT_SPEC.md, section 2). No write path here by design;
/// assignments are only ever created by <see cref="EquipmentHandoverService.FinalizeAsync"/>
/// or, for pre-existing custody, by the Current Inventory import.
/// </summary>
public sealed class EquipmentAssignmentQueryService(ApplicationDbContext dbContext)
{
    public async Task<PagedResult<EquipmentAssignmentRecord>> ListAsync(
        EquipmentAssignmentListFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query =
            from a in dbContext.EquipmentAssignments.AsNoTracking()
            join e in dbContext.Equipment.AsNoTracking() on a.EquipmentId equals e.Id
            join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
            join c in dbContext.Consultants.AsNoTracking() on a.ConsultantId equals c.Id
            select new { Assignment = a, Equipment = e, TypeName = t.Name, ConsultantFullName = c.FullName };

        if (filter.EquipmentId is { } equipmentId)
        {
            query = query.Where(x => x.Assignment.EquipmentId == equipmentId);
        }

        if (filter.ConsultantId is { } consultantId)
        {
            query = query.Where(x => x.Assignment.ConsultantId == consultantId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Assignment.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(x => x.Assignment.StartedAtUtc)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .Select(x => new EquipmentAssignmentRecord(
                x.Assignment.Id, x.Assignment.EquipmentId, x.Equipment.AssetTag, x.TypeName, x.Equipment.Manufacturer, x.Equipment.Model,
                x.Assignment.ConsultantId, x.ConsultantFullName, x.Assignment.Status, x.Assignment.EquipmentHandoverId, x.Assignment.Origin,
                x.Assignment.StartedAtUtc, x.Assignment.EndedAtUtc, x.Equipment.Status))
            .ToListAsync(cancellationToken);

        return new PagedResult<EquipmentAssignmentRecord>(items, page1Based, boundedPageSize, totalCount);
    }
}
