using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Companies;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Companies;

/// <summary>
/// Company management (Phase 2 scope).
/// No hard-delete method exists here by design — companies with historical
/// allocation references must remain readable forever (docs/PROJECT_SPEC.md,
/// section 16).
/// </summary>
public sealed class CompanyService(ApplicationDbContext dbContext, AuditService auditService, TimeProvider timeProvider)
{
    public async Task<PagedResult<CompanyRecord>> ListAsync(
        CompanyListFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Companies.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(c => EF.Functions.ILike(c.Name, $"%{filter.Search}%"));
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(c => c.IsActive == isActive);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderBy(c => c.Name)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .Select(c => ToRecord(c))
            .ToListAsync(cancellationToken);

        return new PagedResult<CompanyRecord>(items, page1Based, boundedPageSize, totalCount);
    }

    public async Task<CompanyRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var company = await dbContext.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return company is null ? null : ToRecord(company);
    }

    public async Task<ServiceResult<CompanyRecord>> CreateAsync(
        CreateCompanyData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        if (!CompanyRules.IsValidName(data.Name))
        {
            return ServiceResult.Fail<CompanyRecord>(ServiceErrorCode.Validation, "Company name is required.");
        }

        var name = data.Name.Trim();
        var normalizedName = CompanyRules.NormalizeName(name);

        if (await dbContext.Companies.AnyAsync(c => c.NormalizedName == normalizedName, cancellationToken))
        {
            return ServiceResult.Fail<CompanyRecord>(ServiceErrorCode.DuplicateName, "A company with this name already exists.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = normalizedName,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        dbContext.Companies.Add(company);

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            return ServiceResult.Fail<CompanyRecord>(ServiceErrorCode.DuplicateName, "A company with this name already exists.");
        }

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.Company.Created,
            EntityType: "Company",
            EntityId: company.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { company.Name }), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(company));
    }

    public async Task<ServiceResult<CompanyRecord>> UpdateAsync(
        Guid id, UpdateCompanyData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var company = await dbContext.Companies.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (company is null)
        {
            return ServiceResult.Fail<CompanyRecord>(ServiceErrorCode.NotFound, "Company not found.");
        }

        if (!CompanyRules.IsValidName(data.Name))
        {
            return ServiceResult.Fail<CompanyRecord>(ServiceErrorCode.Validation, "Company name is required.");
        }

        var name = data.Name.Trim();
        var normalizedName = CompanyRules.NormalizeName(name);

        if (normalizedName != company.NormalizedName &&
            await dbContext.Companies.AnyAsync(c => c.NormalizedName == normalizedName && c.Id != id, cancellationToken))
        {
            return ServiceResult.Fail<CompanyRecord>(ServiceErrorCode.DuplicateName, "A company with this name already exists.");
        }

        var oldName = company.Name;
        company.Name = name;
        company.NormalizedName = normalizedName;
        company.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            return ServiceResult.Fail<CompanyRecord>(ServiceErrorCode.DuplicateName, "A company with this name already exists.");
        }

        if (oldName != company.Name)
        {
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.Company.Updated,
                EntityType: "Company",
                EntityId: company.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                OldValues: new { Name = oldName },
                NewValues: new { company.Name }), cancellationToken);
        }

        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(company));
    }

    public async Task<ServiceResult<CompanyRecord>> SetActiveAsync(
        Guid id, bool active, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var company = await dbContext.Companies.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (company is null)
        {
            return ServiceResult.Fail<CompanyRecord>(ServiceErrorCode.NotFound, "Company not found.");
        }

        if (company.IsActive == active)
        {
            return ServiceResult.Ok(ToRecord(company));
        }

        // Deactivating a company never touches its historical allocations
        // (docs/PROJECT_SPEC.md, section 16) — only NEW external allocations
        // check IsActive (see ConsultantAllocationService), so no cross-entity
        // validation is needed here.
        company.IsActive = active;
        company.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            active ? AuditActions.Company.Activated : AuditActions.Company.Deactivated,
            EntityType: "Company",
            EntityId: company.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(company));
    }

    private static CompanyRecord ToRecord(Company company) =>
        new(company.Id, company.Name, company.IsActive, company.CreatedAtUtc, company.UpdatedAtUtc);
}
