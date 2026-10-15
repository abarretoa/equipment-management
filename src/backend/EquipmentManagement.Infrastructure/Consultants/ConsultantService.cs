using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Consultants;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Handovers;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Consultants;

/// <summary>
/// Consultant profile management (docs/PROJECT_SPEC.md, sections 5, 9-11). No
/// hard-delete method exists here by design — archived consultants keep every
/// historical allocation readable forever.
/// </summary>
public sealed class ConsultantService(ApplicationDbContext dbContext, AuditService auditService, TimeProvider timeProvider)
{
    public async Task<PagedResult<ConsultantSummaryRecord>> ListAsync(
        ConsultantListFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Consultants.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(c => EF.Functions.ILike(c.FullName, $"%{filter.Search}%"));
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(c => c.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Nif))
        {
            var normalizedNif = ConsultantRules.NormalizeNif(filter.Nif);
            query = query.Where(c => c.Nif == normalizedNif);
        }

        if (filter.HasActiveAllocation is { } hasActiveAllocation)
        {
            var eligibleConsultantIds = HandoverConsultantEligibility.EligibleConsultantIds(dbContext);
            query = hasActiveAllocation
                ? query.Where(c => eligibleConsultantIds.Contains(c.Id))
                : query.Where(c => !eligibleConsultantIds.Contains(c.Id));
        }

        if (filter.HasActiveCustody is { } hasActiveCustody)
        {
            // Custody is the Active EquipmentAssignment and nothing else — a
            // Consultant whose allocation has ended still holds what they
            // have not physically returned.
            var custodyHolderIds = dbContext.EquipmentAssignments.AsNoTracking()
                .Where(a => a.Status == AssignmentStatus.Active)
                .Select(a => a.ConsultantId);
            query = hasActiveCustody
                ? query.Where(c => custodyHolderIds.Contains(c.Id))
                : query.Where(c => !custodyHolderIds.Contains(c.Id));
        }

        // Current allocation = the (at most one, by the one-Active invariant)
        // Active-status row for this consultant. A left join keeps consultants
        // with no allocation yet in the result set.
        var projected =
            from c in query
            join a in dbContext.ConsultantAllocations.AsNoTracking().Where(a => a.Status == AllocationStatus.Active)
                on c.Id equals a.ConsultantId into allocations
            from currentAllocation in allocations.DefaultIfEmpty()
            select new { Consultant = c, CurrentAllocation = currentAllocation };

        if (filter.WorkforceType is { } workforceType)
        {
            projected = projected.Where(x => x.CurrentAllocation != null && x.CurrentAllocation.WorkforceType == workforceType);
        }

        if (filter.CompanyId is { } companyId)
        {
            projected = projected.Where(x => x.CurrentAllocation != null && x.CurrentAllocation.CompanyId == companyId);
        }

        var totalCount = await projected.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var page1 = await projected
            .OrderBy(x => x.Consultant.FullName)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .ToListAsync(cancellationToken);

        // Company name is fetched separately (a small, bounded set of ids: at
        // most one per page row) rather than joined above, to keep the main
        // query a simple left join instead of a three-way join.
        var companyIds = page1.Where(x => x.CurrentAllocation?.CompanyId is not null)
            .Select(x => x.CurrentAllocation!.CompanyId!.Value)
            .Distinct()
            .ToList();
        var companyNames = companyIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Companies.AsNoTracking()
                .Where(c => companyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var items = page1.Select(x => new ConsultantSummaryRecord(
            x.Consultant.Id,
            x.Consultant.FullName,
            x.Consultant.IsActive,
            x.Consultant.CreatedAtUtc,
            x.CurrentAllocation?.WorkforceType,
            x.CurrentAllocation?.CompanyId,
            x.CurrentAllocation?.CompanyId is { } id ? companyNames.GetValueOrDefault(id) : null,
            x.CurrentAllocation?.EngagementType,
            x.CurrentAllocation?.RoleOrFunction)).ToList();

        return new PagedResult<ConsultantSummaryRecord>(items, page1Based, boundedPageSize, totalCount);
    }

    public async Task<ConsultantRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var consultant = await dbContext.Consultants.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return consultant is null ? null : ToRecord(consultant);
    }

    public async Task<ServiceResult<ConsultantRecord>> CreateAsync(
        CreateConsultantData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var validation = Validate(data.FullName, data.Nif, data.Email, data.Address, data.Phone, out var normalizedNif);
        if (validation is not null)
        {
            return ServiceResult.Fail<ConsultantRecord>(validation.Value.Code, validation.Value.Message);
        }

        if (await dbContext.Consultants.AnyAsync(c => c.Nif == normalizedNif, cancellationToken))
        {
            return ServiceResult.Fail<ConsultantRecord>(ServiceErrorCode.DuplicateNif, "A consultant with this NIF already exists.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var consultant = new Consultant
        {
            Id = Guid.NewGuid(),
            FullName = data.FullName.Trim(),
            Nif = normalizedNif,
            Email = data.Email.Trim(),
            Address = data.Address.Trim(),
            Phone = data.Phone.Trim(),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        dbContext.Consultants.Add(consultant);

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            return ServiceResult.Fail<ConsultantRecord>(ServiceErrorCode.DuplicateNif, "A consultant with this NIF already exists.");
        }

        // NIF/Address/Phone/Email are never written to the audit trail, even as
        // "new values" on creation (Phase 2 scope) — only that a consultant profile was created, by whom.
        await auditService.RecordAsync(new AuditEntry(
            AuditActions.Consultant.Created,
            EntityType: "Consultant",
            EntityId: consultant.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { consultant.FullName }), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(consultant));
    }

    public async Task<ServiceResult<ConsultantRecord>> UpdateAsync(
        Guid id, UpdateConsultantData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var consultant = await dbContext.Consultants.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (consultant is null)
        {
            return ServiceResult.Fail<ConsultantRecord>(ServiceErrorCode.NotFound, "Consultant not found.");
        }

        var validation = Validate(data.FullName, data.Nif, data.Email, data.Address, data.Phone, out var normalizedNif);
        if (validation is not null)
        {
            return ServiceResult.Fail<ConsultantRecord>(validation.Value.Code, validation.Value.Message);
        }

        if (normalizedNif != consultant.Nif &&
            await dbContext.Consultants.AnyAsync(c => c.Nif == normalizedNif && c.Id != id, cancellationToken))
        {
            return ServiceResult.Fail<ConsultantRecord>(ServiceErrorCode.DuplicateNif, "A consultant with this NIF already exists.");
        }

        // FullName is not PII-classified (Phase 2 scope's sensitive list is NIF/Address/Phone/Email only),
        // so its before/after values are safe to audit directly. The other four
        // are recorded only by name, never by value.
        var sensitiveChangedFields = new List<string>();
        var fullNameChanged = consultant.FullName.Trim() != data.FullName.Trim();
        if (consultant.Nif != normalizedNif)
        {
            sensitiveChangedFields.Add("nif");
        }

        if (consultant.Email.Trim() != data.Email.Trim())
        {
            sensitiveChangedFields.Add("email");
        }

        if (consultant.Address.Trim() != data.Address.Trim())
        {
            sensitiveChangedFields.Add("address");
        }

        if (consultant.Phone.Trim() != data.Phone.Trim())
        {
            sensitiveChangedFields.Add("phone");
        }

        var oldFullName = consultant.FullName;
        consultant.FullName = data.FullName.Trim();
        consultant.Nif = normalizedNif;
        consultant.Email = data.Email.Trim();
        consultant.Address = data.Address.Trim();
        consultant.Phone = data.Phone.Trim();
        consultant.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            return ServiceResult.Fail<ConsultantRecord>(ServiceErrorCode.DuplicateNif, "A consultant with this NIF already exists.");
        }

        if (fullNameChanged || sensitiveChangedFields.Count > 0)
        {
            var metadata = new Dictionary<string, object>();
            if (fullNameChanged)
            {
                metadata["fullName"] = new { old = oldFullName, @new = consultant.FullName };
            }

            if (sensitiveChangedFields.Count > 0)
            {
                metadata["changedFields"] = sensitiveChangedFields;
            }

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.Consultant.Updated,
                EntityType: "Consultant",
                EntityId: consultant.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                Metadata: metadata), cancellationToken);
        }

        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(consultant));
    }

    public async Task<ServiceResult<ConsultantRecord>> SetActiveAsync(
        Guid id, bool active, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var consultant = await dbContext.Consultants.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (consultant is null)
        {
            return ServiceResult.Fail<ConsultantRecord>(ServiceErrorCode.NotFound, "Consultant not found.");
        }

        if (consultant.IsActive == active)
        {
            return ServiceResult.Ok(ToRecord(consultant));
        }

        // Deactivating never touches existing allocations (Phase 2 scope) — only NEW allocations are blocked
        // for an inactive consultant (see ConsultantAllocationService).
        consultant.IsActive = active;
        consultant.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            active ? AuditActions.Consultant.Activated : AuditActions.Consultant.Deactivated,
            EntityType: "Consultant",
            EntityId: consultant.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(consultant));
    }

    private static (ServiceErrorCode Code, string Message)? Validate(
        string fullName, string nif, string email, string address, string phone, out string normalizedNif)
    {
        normalizedNif = ConsultantRules.NormalizeNif(nif);

        if (!ConsultantRules.IsValidFullName(fullName))
        {
            return (ServiceErrorCode.Validation, "Full name is required.");
        }

        if (!ConsultantRules.IsValidNormalizedNif(normalizedNif))
        {
            return (ServiceErrorCode.Validation, "NIF is required and must be a plausible tax identifier.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return (ServiceErrorCode.Validation, "Email is required.");
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            return (ServiceErrorCode.Validation, "Address is required.");
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            return (ServiceErrorCode.Validation, "Phone is required.");
        }

        return null;
    }

    private static ConsultantRecord ToRecord(Consultant consultant) => new(
        consultant.Id, consultant.FullName, consultant.Nif, consultant.Email, consultant.Address,
        consultant.Phone, consultant.IsActive, consultant.CreatedAtUtc, consultant.UpdatedAtUtc);
}
