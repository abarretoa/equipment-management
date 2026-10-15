using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Returns;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Returns;

/// <summary>
/// Read-only views of returns as HR works with them: one row per
/// <see cref="EquipmentReturnGroup"/> (one initiation event, however many
/// items), plus one row per return that has no group because it predates
/// grouping. Nothing here is a second source of truth — every status and date
/// shown for a group is derived from its returns and from the allocation that
/// created it. <c>EquipmentReturnService.ListAsync</c> remains the flat
/// per-return list, which is what counts and statistics are based on.
/// </summary>
public sealed class EquipmentReturnGroupQueryService(ApplicationDbContext dbContext)
{
    public async Task<PagedResult<EquipmentReturnListRowRecord>> ListOverviewAsync(
        EquipmentReturnOverviewFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // A row is a group, or an ungrouped return standing for itself. Each
        // row is summarised over ALL of its returns, so that the status filter
        // below works on the row as a whole. Projected into an anonymous type:
        // EF Core cannot translate filtering/ordering chained after a
        // GroupBy-into-record projection.
        var rows = dbContext.EquipmentReturns.AsNoTracking()
            .GroupBy(r => r.ReturnGroupId ?? r.Id)
            .Select(g => new
            {
                RowKey = g.Key,
                LastCreatedAtUtc = g.Max(r => r.CreatedAtUtc),
                NotCancelledCount = g.Count(r => r.Status != ReturnStatus.Cancelled),
                PendingCount = g.Count(r => r.Status == ReturnStatus.Pending),
                ReceivedCount = g.Count(r => r.Status == ReturnStatus.Received),
            });

        // The status filter puts every row in exactly one bucket — the SQL
        // form of ReturnRules.DeriveListFilterStatus, which the integration
        // tests hold it to. A group is filtered by its derived collection
        // state, never by "any of its returns matches": a partly received
        // group is Pending, and only Pending.
        rows = filter.Status switch
        {
            ReturnStatus.Cancelled => rows.Where(x => x.NotCancelledCount == 0),
            ReturnStatus.Pending => rows.Where(x => x.PendingCount > 0),
            ReturnStatus.Received => rows.Where(x => x.NotCancelledCount > 0 && x.PendingCount == 0 && x.ReceivedCount > 0),
            ReturnStatus.Completed => rows.Where(x => x.NotCancelledCount > 0 && x.PendingCount == 0 && x.ReceivedCount == 0),
            _ => rows,
        };

        // Search is different in kind: it finds a row by any of its items.
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var matchingKeys =
                from r in dbContext.EquipmentReturns.AsNoTracking()
                join e in dbContext.Equipment.AsNoTracking() on r.EquipmentId equals e.Id
                join c in dbContext.Consultants.AsNoTracking() on r.ConsultantId equals c.Id
                where EF.Functions.ILike(c.FullName, $"%{filter.Search}%") || EF.Functions.ILike(e.AssetTag, $"%{filter.Search}%")
                select r.ReturnGroupId ?? r.Id;

            rows = rows.Where(x => matchingKeys.Contains(x.RowKey));
        }

        var totalCount = await rows.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var pageKeys = await rows
            .OrderByDescending(x => x.LastCreatedAtUtc)
            .ThenBy(x => x.RowKey)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .Select(x => x.RowKey)
            .ToListAsync(cancellationToken);

        // A picked group is shown whole, with all of its returns.
        var members = await LoadMembersAsync(r => pageKeys.Contains(r.ReturnGroupId ?? r.Id), cancellationToken);
        var groups = await LoadGroupsAsync(members, cancellationToken);

        var result = new List<EquipmentReturnListRowRecord>(pageKeys.Count);
        foreach (var key in pageKeys)
        {
            if (groups.TryGetValue(key, out var group))
            {
                result.Add(new EquipmentReturnListRowRecord(BuildGroupRecord(group, members.Where(m => m.Return.ReturnGroupId == key).ToList()), null));
            }
            else if (members.FirstOrDefault(m => m.Return.Id == key) is { } single)
            {
                result.Add(new EquipmentReturnListRowRecord(null, ToSummaryRecord(single)));
            }
        }

        return new PagedResult<EquipmentReturnListRowRecord>(result, page1Based, boundedPageSize, totalCount);
    }

    public async Task<EquipmentReturnGroupDetailRecord?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var members = await LoadMembersAsync(r => r.ReturnGroupId == groupId, cancellationToken);
        var groups = await LoadGroupsAsync(members, cancellationToken);
        if (!groups.TryGetValue(groupId, out var group))
        {
            return null;
        }

        var consultantId = group.Group.ConsultantId;
        var otherPending = await LoadMembersAsync(
            r => r.ConsultantId == consultantId && r.Status == ReturnStatus.Pending && r.ReturnGroupId != groupId, cancellationToken);

        return new EquipmentReturnGroupDetailRecord(
            BuildGroupRecord(group, members),
            otherPending.OrderBy(m => m.AssetTag, StringComparer.Ordinal).Select(ToSummaryRecord).ToList());
    }

    private sealed record Member(
        EquipmentReturn Return, string AssetTag, string EquipmentTypeName, string? Manufacturer, string? Model,
        Domain.Equipments.EquipmentStatus EquipmentStatus, string ConsultantFullName);

    private sealed record LoadedGroup(EquipmentReturnGroup Group, ConsultantAllocation? Allocation);

    private async Task<List<Member>> LoadMembersAsync(
        System.Linq.Expressions.Expression<Func<EquipmentReturn, bool>> predicate, CancellationToken cancellationToken) =>
        await (
            from r in dbContext.EquipmentReturns.AsNoTracking().Where(predicate)
            join e in dbContext.Equipment.AsNoTracking() on r.EquipmentId equals e.Id
            join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
            join c in dbContext.Consultants.AsNoTracking() on r.ConsultantId equals c.Id
            select new Member(r, e.AssetTag, t.Name, e.Manufacturer, e.Model, e.Status, c.FullName)).ToListAsync(cancellationToken);

    private async Task<Dictionary<Guid, LoadedGroup>> LoadGroupsAsync(List<Member> members, CancellationToken cancellationToken)
    {
        var groupIds = members.Where(m => m.Return.ReturnGroupId is not null).Select(m => m.Return.ReturnGroupId!.Value).Distinct().ToList();
        if (groupIds.Count == 0)
        {
            return [];
        }

        var groups = await dbContext.EquipmentReturnGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToListAsync(cancellationToken);

        var allocationIds = groups.Where(g => g.ConsultantAllocationId is not null).Select(g => g.ConsultantAllocationId!.Value).ToList();
        var allocations = allocationIds.Count == 0
            ? []
            : await dbContext.ConsultantAllocations.AsNoTracking()
                .Where(a => allocationIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, cancellationToken);

        return groups.ToDictionary(
            g => g.Id,
            g => new LoadedGroup(g, g.ConsultantAllocationId is { } id ? allocations.GetValueOrDefault(id) : null));
    }

    private static EquipmentReturnGroupRecord BuildGroupRecord(LoadedGroup loaded, List<Member> members)
    {
        var group = loaded.Group;
        var isEndOfEngagement = group.ConsultantAllocationId is not null;

        // The expected date is never stored on the group: an end of
        // engagement's is the allocation's collection window, a manual
        // return's is the date its returns were started with.
        var expectedFrom = isEndOfEngagement
            ? loaded.Allocation?.ExpectedCollectionFromDate
            : members.Where(m => m.Return.ExpectedReturnDate is not null).Min(m => m.Return.ExpectedReturnDate);
        var expectedTo = isEndOfEngagement ? loaded.Allocation?.ExpectedCollectionToDate : null;

        var items = members
            .OrderBy(m => m.AssetTag, StringComparer.Ordinal)
            .Select(m => new EquipmentReturnGroupItemRecord(
                m.Return.Id, m.Return.EquipmentId, m.AssetTag, m.EquipmentTypeName, m.Manufacturer, m.Model,
                m.Return.Status, m.EquipmentStatus, m.Return.ExpectedReturnDate, m.Return.ReceivedOn))
            .ToList();

        return new EquipmentReturnGroupRecord(
            group.Id,
            group.ConsultantId,
            members.Count > 0 ? members[0].ConsultantFullName : string.Empty,
            isEndOfEngagement,
            ReturnRules.DeriveGroupCollectionStatus(members.Select(m => m.Return.Status)),
            expectedFrom,
            expectedTo,
            members.Count(m => m.Return.Status is ReturnStatus.Received or ReturnStatus.Completed),
            group.CreatedByDisplayNameSnapshot,
            group.CreatedAtUtc,
            items);
    }

    private static EquipmentReturnSummaryRecord ToSummaryRecord(Member m) => new(
        m.Return.Id, m.Return.EquipmentAssignmentId, m.Return.EquipmentId, m.AssetTag, m.Return.ConsultantId, m.ConsultantFullName,
        m.Return.Status, m.Return.Reason, m.Return.ExpectedReturnDate, m.Return.ReceivedOn, m.Return.CreatedAtUtc, m.Return.CompletedAtUtc);
}
