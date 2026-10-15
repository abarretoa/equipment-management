using System.Data;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.Returns;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.Infrastructure.Returns;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Allocations;

/// <summary>
/// Historical allocation management (docs/PROJECT_SPEC.md, sections 6, 21;
/// Phase 2 scope). There is deliberately no method here that can
/// rewrite a historical (Ended, or Company/WorkforceType/StartDate on any)
/// allocation — see <see cref="UpdateAllocationData"/>'s remarks and ADR-021.
/// </summary>
public sealed class ConsultantAllocationService(
    ApplicationDbContext dbContext, AuditService auditService, ConsultantAllocationCollectionNotifier collectionNotifier,
    EquipmentReturnInitiator returnInitiator, TimeProvider timeProvider)
{
    /// <summary>Partial unique index on ConsultantId WHERE Status = 'Active' (see ApplicationDbContext) — the DB-level backstop for one Active allocation per Consultant.</summary>
    private const string OneActiveAllocationIndexName = "IX_ConsultantAllocations_ConsultantId_Active";

    private const string ActiveAllocationExistsMessage =
        "Este consultor já tem uma alocação ativa. Termine a alocação atual antes de registar uma nova.";

    private const string ConcurrentChangeMessage =
        "Esta alocação ou um dos equipamentos do consultor foi alterado por outra operação. Atualize a página e tente novamente.";

    /// <summary>Stored in the existing free-text <see cref="EquipmentReturn.Reason"/> of every return the end-of-engagement operation initiates, so HR can see on the return itself why it exists.</summary>
    private const string EndOfEngagementReturnReason = "Fim de vínculo";

    public async Task<PagedResult<ConsultantAllocationRecord>> ListForConsultantAsync(
        Guid consultantId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.ConsultantAllocations.AsNoTracking().Where(a => a.ConsultantId == consultantId);

        var totalCount = await query.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var page1 = await query
            .OrderByDescending(a => a.StartDate)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .ToListAsync(cancellationToken);

        var items = await ToRecordsAsync(page1, cancellationToken);

        return new PagedResult<ConsultantAllocationRecord>(items, page1Based, boundedPageSize, totalCount);
    }

    public async Task<ConsultantAllocationRecord?> GetByIdAsync(
        Guid consultantId, Guid allocationId, CancellationToken cancellationToken = default)
    {
        var allocation = await dbContext.ConsultantAllocations.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == allocationId && a.ConsultantId == consultantId, cancellationToken);

        if (allocation is null)
        {
            return null;
        }

        var records = await ToRecordsAsync([allocation], cancellationToken);
        return records[0];
    }

    /// <summary>The current (Active-status) allocation, if any — at most one by the one-Active-allocation invariant (AllocationRules.CanCreateActiveAllocation + its partial unique index).</summary>
    public async Task<ConsultantAllocationRecord?> GetCurrentAsync(Guid consultantId, CancellationToken cancellationToken = default)
    {
        var allocation = await dbContext.ConsultantAllocations.AsNoTracking()
            .Where(a => a.ConsultantId == consultantId && a.Status == AllocationStatus.Active)
            .OrderByDescending(a => a.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (allocation is null)
        {
            return null;
        }

        var records = await ToRecordsAsync([allocation], cancellationToken);
        return records[0];
    }

    public async Task<ServiceResult<ConsultantAllocationRecord>> CreateAsync(
        Guid consultantId, CreateAllocationData data, Guid actorUserId, string actorDisplayName,
        CancellationToken cancellationToken = default)
    {
        if (!AllocationRules.IsValidRoleOrFunction(data.RoleOrFunction))
        {
            return ServiceResult.Fail<ConsultantAllocationRecord>(ServiceErrorCode.Validation, "Role/function is required.");
        }

        if (!AllocationRules.IsCompanyRequirementSatisfied(data.WorkforceType, data.CompanyId))
        {
            var message = data.WorkforceType == WorkforceType.External
                ? "External allocations require a company."
                : "Internal allocations must not reference a company.";
            return ServiceResult.Fail<ConsultantAllocationRecord>(ServiceErrorCode.Validation, message);
        }

        if (!AllocationRules.IsValidDateRange(data.StartDate, data.EndDate))
        {
            return ServiceResult.Fail<ConsultantAllocationRecord>(ServiceErrorCode.Validation, "End date cannot be earlier than start date.");
        }

        var consultant = await dbContext.Consultants.AsNoTracking().FirstOrDefaultAsync(c => c.Id == consultantId, cancellationToken);
        if (consultant is null)
        {
            return ServiceResult.Fail<ConsultantAllocationRecord>(ServiceErrorCode.NotFound, "Consultant not found.");
        }

        if (!consultant.IsActive)
        {
            return ServiceResult.Fail<ConsultantAllocationRecord>(
                ServiceErrorCode.InactiveReference, "An inactive consultant cannot receive a new allocation.");
        }

        if (data.WorkforceType == WorkforceType.External)
        {
            var company = await dbContext.Companies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == data.CompanyId!.Value, cancellationToken);
            if (company is null)
            {
                return ServiceResult.Fail<ConsultantAllocationRecord>(ServiceErrorCode.Validation, "Company not found.");
            }

            if (!company.IsActive)
            {
                return ServiceResult.Fail<ConsultantAllocationRecord>(
                    ServiceErrorCode.InactiveReference, "An inactive company cannot be used for a new allocation.");
            }
        }

        // Serializable isolation closes the race where two concurrent requests
        // both see "no active allocation" and both insert one — the same
        // pattern used for the last-Administration safeguard in Phase 1 (see
        // UserManagementService / ADR-016). The partial unique
        // index on ConsultantId WHERE Status = 'Active' is the final backstop.
        //
        // The conflict Postgres raises for this can surface as a raw
        // PostgresException at CommitAsync, or DbUpdateException-wrapped during
        // SaveChangesAsync (see PostgresConflictClassifier's remarks and the
        // ADR-036) — the catch below walks the exception
        // chain instead of matching one shape.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // One Active allocation per Consultant, decided by lifecycle status
        // alone: a future-dated Active allocation counts exactly like a current
        // one (AllocationRules.CanCreateActiveAllocation).
        var activeCount = await dbContext.ConsultantAllocations.AsNoTracking()
            .CountAsync(a => a.ConsultantId == consultantId && a.Status == AllocationStatus.Active, cancellationToken);

        if (!AllocationRules.CanCreateActiveAllocation(activeCount))
        {
            return ServiceResult.Fail<ConsultantAllocationRecord>(ServiceErrorCode.ActiveAllocationAlreadyExists, ActiveAllocationExistsMessage);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var allocation = new ConsultantAllocation
        {
            Id = Guid.NewGuid(),
            ConsultantId = consultantId,
            CompanyId = data.CompanyId,
            WorkforceType = data.WorkforceType,
            EngagementType = data.EngagementType,
            RoleOrFunction = data.RoleOrFunction.Trim(),
            StartDate = data.StartDate,
            EndDate = data.EndDate,
            Status = AllocationStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        dbContext.ConsultantAllocations.Add(allocation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            // Inside the transaction: the allocation and its audit row commit together.
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ConsultantAllocation.Created,
                EntityType: "ConsultantAllocation",
                EntityId: allocation.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new
                {
                    ConsultantId = consultantId,
                    allocation.CompanyId,
                    WorkforceType = allocation.WorkforceType.ToString(),
                    EngagementType = allocation.EngagementType.ToString(),
                    allocation.RoleOrFunction,
                    allocation.StartDate,
                    allocation.EndDate,
                }), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolationOn(ex, OneActiveAllocationIndexName))
        {
            // The partial unique index rejected a second Active allocation that
            // a concurrent request committed first.
            return ServiceResult.Fail<ConsultantAllocationRecord>(ServiceErrorCode.ActiveAllocationAlreadyExists, ActiveAllocationExistsMessage);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsSerializationFailure(ex))
        {
            return ServiceResult.Fail<ConsultantAllocationRecord>(
                ServiceErrorCode.Concurrency, "Another allocation change is in progress for this consultant; please retry.");
        }

        var records = await ToRecordsAsync([allocation], cancellationToken);
        return ServiceResult.Ok(records[0]);
    }

    public async Task<ServiceResult<ConsultantAllocationRecord>> UpdateRoleOrFunctionAsync(
        Guid consultantId, Guid allocationId, UpdateAllocationData data, Guid actorUserId, string actorDisplayName,
        CancellationToken cancellationToken = default)
    {
        var allocation = await dbContext.ConsultantAllocations
            .FirstOrDefaultAsync(a => a.Id == allocationId && a.ConsultantId == consultantId, cancellationToken);
        if (allocation is null)
        {
            return ServiceResult.Fail<ConsultantAllocationRecord>(ServiceErrorCode.NotFound, "Allocation not found.");
        }

        if (allocation.Status != AllocationStatus.Active)
        {
            return ServiceResult.Fail<ConsultantAllocationRecord>(
                ServiceErrorCode.AllocationNotActive, "Ended allocations cannot be modified. Create a new allocation instead.");
        }

        if (!AllocationRules.IsValidRoleOrFunction(data.RoleOrFunction))
        {
            return ServiceResult.Fail<ConsultantAllocationRecord>(ServiceErrorCode.Validation, "Role/function is required.");
        }

        var oldRoleOrFunction = allocation.RoleOrFunction;
        var newRoleOrFunction = data.RoleOrFunction.Trim();

        if (oldRoleOrFunction != newRoleOrFunction)
        {
            allocation.RoleOrFunction = newRoleOrFunction;
            allocation.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

            await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ConsultantAllocation.Updated,
                EntityType: "ConsultantAllocation",
                EntityId: allocation.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                OldValues: new { RoleOrFunction = oldRoleOrFunction },
                NewValues: new { RoleOrFunction = newRoleOrFunction }), cancellationToken);
            await transaction.CommitIfOwnedAsync(cancellationToken);
        }

        var records = await ToRecordsAsync([allocation], cancellationToken);
        return ServiceResult.Ok(records[0]);
    }

    /// <summary>
    /// Ending an allocation is not always offboarding — see
    /// <see cref="EndAllocationData"/>'s remarks. When
    /// <see cref="EndAllocationData.IsEndOfEngagement"/> is false, nothing but
    /// the allocation changes: no return is started, no equipment or
    /// assignment is touched, no email is sent.
    ///
    /// When it is true (Iteration 4), every piece of equipment in
    /// the Consultant's active custody — discovered exclusively through their
    /// <c>Active</c> <see cref="EquipmentAssignment"/> rows — gets its own
    /// <see cref="EquipmentReturn"/>, unless one is already pending, which is
    /// left untouched. The equipment moves to <c>ReturnPending</c>; the
    /// assignment stays <c>Active</c>, because custody ends only at physical
    /// receipt (<c>EquipmentReturnService.ReceiveAsync</c>). Ending the
    /// allocation and creating the returns is one atomic unit: if any item is
    /// in a state that allows neither, nothing is persisted at all.
    ///
    /// The single consolidated collection email is communication, sent after
    /// the commit — a send failure never rolls back the ending or the returns.
    /// </summary>
    public async Task<ServiceResult<EndAllocationResult>> EndAsync(
        Guid consultantId, Guid allocationId, EndAllocationData data, Guid actorUserId, string actorDisplayName,
        CancellationToken cancellationToken = default)
    {
        EndOutcome outcome;
        try
        {
            var result = await EndInTransactionAsync(consultantId, allocationId, data, actorUserId, actorDisplayName, cancellationToken);
            if (!result.Succeeded)
            {
                return ServiceResult.Fail<EndAllocationResult>(result.ErrorCode, result.ErrorMessage!);
            }

            outcome = result.Value!;
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolationOn(ex, EquipmentReturnInitiator.OneOpenReturnPerAssignmentIndexName))
        {
            // A return for one of these assignments was started by another
            // request after this transaction read it as having none. The
            // partial unique index rejected the duplicate and the whole
            // operation rolled back; a retry will find that return pending.
            return ServiceResult.Fail<EndAllocationResult>(ServiceErrorCode.Concurrency, ConcurrentChangeMessage);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsSerializationFailure(ex))
        {
            return ServiceResult.Fail<EndAllocationResult>(ServiceErrorCode.Concurrency, ConcurrentChangeMessage);
        }

        if (outcome.Custody.Count > 0)
        {
            var consultant = await dbContext.Consultants.AsNoTracking().FirstAsync(c => c.Id == consultantId, cancellationToken);
            await collectionNotifier.SendAsync(
                outcome.Allocation, consultant, outcome.Custody.Select(x => (x.Equipment, x.Type)).ToList(), actorUserId, actorDisplayName,
                cancellationToken);
        }

        var records = await ToRecordsAsync([outcome.Allocation], cancellationToken);
        return ServiceResult.Ok(new EndAllocationResult(
            records[0], outcome.ReturnsCreatedCount, outcome.ReturnsAlreadyPendingCount, outcome.ReturnGroupId));
    }

    /// <summary>
    /// Every row this method decides on or mutates is loaded AFTER opening the
    /// Serializable transaction (same discipline as
    /// <c>EquipmentReturnService.ReceiveAsync</c>; see ADR-042), so a concurrent end of the same allocation, or a concurrent manual
    /// return/receipt/cancellation for one of the same assignments, surfaces as
    /// a Postgres conflict instead of a lost update. Audit entries are written
    /// inside the transaction, so they exist exactly when the change does.
    /// Returning a failure disposes the transaction uncommitted.
    /// </summary>
    private async Task<ServiceResult<EndOutcome>> EndInTransactionAsync(
        Guid consultantId, Guid allocationId, EndAllocationData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var allocation = await dbContext.ConsultantAllocations
            .FirstOrDefaultAsync(a => a.Id == allocationId && a.ConsultantId == consultantId, cancellationToken);
        if (allocation is null)
        {
            return ServiceResult.Fail<EndOutcome>(ServiceErrorCode.NotFound, "Allocation not found.");
        }

        if (allocation.Status != AllocationStatus.Active)
        {
            return ServiceResult.Fail<EndOutcome>(ServiceErrorCode.AllocationNotActive, "This allocation has already ended.");
        }

        if (!AllocationRules.IsValidDateRange(allocation.StartDate, data.EndDate))
        {
            return ServiceResult.Fail<EndOutcome>(ServiceErrorCode.Validation, "End date cannot be earlier than start date.");
        }

        if (data.IsEndOfEngagement is null)
        {
            return ServiceResult.Fail<EndOutcome>(
                ServiceErrorCode.Validation,
                "É necessário indicar se este encerramento corresponde ao fim do vínculo do consultor.");
        }

        var isEndOfEngagement = data.IsEndOfEngagement.Value;

        // Current custody is the Consultant's Active assignments and nothing
        // else. Equipment is tracked: initiating a return changes its status.
        List<CustodyItem> custody = isEndOfEngagement
            ? await (
                from a in dbContext.EquipmentAssignments
                join e in dbContext.Equipment on a.EquipmentId equals e.Id
                join t in dbContext.EquipmentTypes on e.EquipmentTypeId equals t.Id
                where a.ConsultantId == consultantId && a.Status == AssignmentStatus.Active
                orderby e.AssetTag
                select new CustodyItem(a, e, t)).ToListAsync(cancellationToken)
            : [];

        var hasCustody = custody.Count > 0;

        if (hasCustody && data.ExpectedCollectionFromDate is null)
        {
            return ServiceResult.Fail<EndOutcome>(
                ServiceErrorCode.CollectionEstimateRequired,
                "É necessário indicar uma previsão de recolha, uma vez que o consultor tem equipamento atualmente atribuído.");
        }

        if (hasCustody && data.ExpectedCollectionToDate is { } to && to < data.ExpectedCollectionFromDate)
        {
            return ServiceResult.Fail<EndOutcome>(
                ServiceErrorCode.Validation, "A data final da previsão de recolha não pode ser anterior à data inicial.");
        }

        // At most one non-Cancelled return per assignment (partial unique
        // index), so keying by assignment is safe.
        var assignmentIds = custody.Select(x => x.Assignment.Id).ToList();
        var openReturns = hasCustody
            ? await dbContext.EquipmentReturns.AsNoTracking()
                .Where(r => assignmentIds.Contains(r.EquipmentAssignmentId) && r.Status != ReturnStatus.Cancelled)
                .ToDictionaryAsync(r => r.EquipmentAssignmentId, cancellationToken)
            : [];

        var dispositions = custody.ToDictionary(
            x => x.Assignment.Id,
            x => ReturnRules.ClassifyForEndOfEngagement(x.Equipment.Status, openReturns.GetValueOrDefault(x.Assignment.Id)?.Status));

        var requiringAttention = custody
            .Where(x => dispositions[x.Assignment.Id] == EndOfEngagementReturnDisposition.RequiresAttention)
            .Select(x => x.Equipment.AssetTag)
            .ToList();
        if (requiringAttention.Count > 0)
        {
            return ServiceResult.Fail<EndOutcome>(ServiceErrorCode.EquipmentRequiresAttention, BuildRequiresAttentionMessage(requiringAttention));
        }

        var toInitiate = custody.Where(x => dispositions[x.Assignment.Id] == EndOfEngagementReturnDisposition.Initiate).ToList();
        if (toInitiate.Count > 0 && !data.ActorMayInitiateReturns)
        {
            return ServiceResult.Fail<EndOutcome>(
                ServiceErrorCode.ReturnPermissionRequired,
                "Não tem permissão para iniciar devoluções de equipamento, necessária para encerrar o vínculo de um consultor com equipamento atribuído.");
        }

        // One group for this collection event, linked to the allocation being
        // ended — and only when it creates at least one return: a group with
        // no returns would represent nothing. Returns that were already
        // pending keep the group (or no group) they were created with.
        var group = toInitiate.Count > 0
            ? returnInitiator.StageGroup(consultantId, allocation.Id, actorUserId, actorDisplayName)
            : null;

        var created = new List<(EquipmentReturn Return, Equipment Equipment)>();
        foreach (var item in toInitiate)
        {
            // The expected return date is the start of the collection window
            // HR just provided; the window itself stays on the allocation.
            var staged = returnInitiator.Stage(
                group!, item.Assignment, item.Equipment, hasOpenReturn: false,
                new InitiateReturnData(data.ExpectedCollectionFromDate, EndOfEngagementReturnReason), actorUserId, actorDisplayName);
            if (!staged.Succeeded)
            {
                return ServiceResult.Fail<EndOutcome>(
                    ServiceErrorCode.EquipmentRequiresAttention, BuildRequiresAttentionMessage([item.Equipment.AssetTag]));
            }

            created.Add((staged.Value!, item.Equipment));
        }

        var alreadyPendingReturnIds = custody
            .Where(x => dispositions[x.Assignment.Id] == EndOfEngagementReturnDisposition.AlreadyPending)
            .Select(x => openReturns[x.Assignment.Id].Id)
            .ToList();

        allocation.Status = AllocationStatus.Ended;
        allocation.EndDate = data.EndDate;
        allocation.IsEndOfEngagement = isEndOfEngagement;
        allocation.ExpectedCollectionFromDate = hasCustody ? data.ExpectedCollectionFromDate : null;
        allocation.ExpectedCollectionToDate = hasCustody ? data.ExpectedCollectionToDate : null;
        allocation.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.ConsultantAllocation.Ended,
            EntityType: "ConsultantAllocation",
            EntityId: allocation.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { allocation.EndDate, allocation.IsEndOfEngagement }), cancellationToken);

        if (hasCustody)
        {
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ConsultantAllocation.CollectionEstimateRecorded,
                EntityType: "ConsultantAllocation",
                EntityId: allocation.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { allocation.ExpectedCollectionFromDate, allocation.ExpectedCollectionToDate }), cancellationToken);

            if (group is not null)
            {
                await returnInitiator.RecordCreatedAsync(
                    group, created, EquipmentReturnInitiator.EndOfEngagementTrigger, actorUserId, actorDisplayName, cancellationToken);
            }

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ConsultantAllocation.EndOfEngagementReturnsInitiated,
                EntityType: "ConsultantAllocation",
                EntityId: allocation.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new
                {
                    ReturnGroupId = group?.Id,
                    CreatedReturnIds = created.Select(x => x.Return.Id).ToList(),
                    AlreadyPendingReturnIds = alreadyPendingReturnIds,
                }), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return ServiceResult.Ok(new EndOutcome(allocation, custody, created.Count, alreadyPendingReturnIds.Count, group?.Id));
    }

    private static string BuildRequiresAttentionMessage(List<string> assetTags) =>
        "Não foi possível encerrar o vínculo: " +
        (assetTags.Count == 1
            ? $"o equipamento {assetTags[0]} requer atenção"
            : $"os equipamentos {string.Join(", ", assetTags)} requerem atenção") +
        " antes de a devolução poder ser iniciada. Verifique o estado e as devoluções em curso e tente novamente.";

    private sealed record CustodyItem(EquipmentAssignment Assignment, Equipment Equipment, EquipmentType Type);

    private sealed record EndOutcome(
        ConsultantAllocation Allocation, IReadOnlyList<CustodyItem> Custody, int ReturnsCreatedCount, int ReturnsAlreadyPendingCount,
        Guid? ReturnGroupId);

    private async Task<IReadOnlyList<ConsultantAllocationRecord>> ToRecordsAsync(
        IReadOnlyList<Domain.Allocations.ConsultantAllocation> allocations, CancellationToken cancellationToken)
    {
        var companyIds = allocations.Where(a => a.CompanyId is not null).Select(a => a.CompanyId!.Value).Distinct().ToList();
        var companyNames = companyIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Companies.AsNoTracking()
                .Where(c => companyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return allocations.Select(a => new ConsultantAllocationRecord(
            a.Id,
            a.ConsultantId,
            a.CompanyId,
            a.CompanyId is { } id ? companyNames.GetValueOrDefault(id) : null,
            a.WorkforceType,
            a.EngagementType,
            a.RoleOrFunction,
            a.StartDate,
            a.EndDate,
            a.Status,
            a.IsEndOfEngagement,
            a.ExpectedCollectionFromDate,
            a.ExpectedCollectionToDate,
            a.CreatedAtUtc,
            a.UpdatedAtUtc)).ToList();
    }
}
