using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Equipments;

/// <summary>
/// EquipmentType reference-data management (docs/PROJECT_SPEC.md, section 7).
/// No hard-delete method exists here by design — types with historical
/// equipment references must remain readable forever (docs/PROJECT_SPEC.md,
/// section 16).
/// </summary>
public sealed class EquipmentTypeService(ApplicationDbContext dbContext, AuditService auditService, TimeProvider timeProvider)
{
    public async Task<PagedResult<EquipmentTypeRecord>> ListAsync(
        EquipmentTypeListFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.EquipmentTypes.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(t => EF.Functions.ILike(t.Name, $"%{filter.Search}%"));
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(t => t.IsActive == isActive);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderBy(t => t.Name)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .Select(t => ToRecord(t))
            .ToListAsync(cancellationToken);

        return new PagedResult<EquipmentTypeRecord>(items, page1Based, boundedPageSize, totalCount);
    }

    public async Task<EquipmentTypeRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return type is null ? null : ToRecord(type);
    }

    public async Task<ServiceResult<EquipmentTypeRecord>> CreateAsync(
        CreateEquipmentTypeData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        if (!EquipmentTypeRules.IsValidName(data.Name))
        {
            return ServiceResult.Fail<EquipmentTypeRecord>(ServiceErrorCode.Validation, "Name is required.");
        }

        var normalizedPrefix = EquipmentTypeRules.NormalizePrefix(data.AssetTagPrefix);
        if (!EquipmentTypeRules.IsValidPrefix(normalizedPrefix))
        {
            return ServiceResult.Fail<EquipmentTypeRecord>(
                ServiceErrorCode.Validation, "AssetTagPrefix must be 2-6 letters (e.g. \"LT\").");
        }

        var name = data.Name.Trim();
        var normalizedName = EquipmentTypeRules.NormalizeName(name);

        if (await dbContext.EquipmentTypes.AnyAsync(t => t.NormalizedName == normalizedName, cancellationToken))
        {
            return ServiceResult.Fail<EquipmentTypeRecord>(ServiceErrorCode.DuplicateName, "An equipment type with this name already exists.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var type = new EquipmentType
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = normalizedName,
            Kind = data.Kind,
            AssetTagPrefix = normalizedPrefix,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        dbContext.EquipmentTypes.Add(type);

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            return ServiceResult.Fail<EquipmentTypeRecord>(ServiceErrorCode.DuplicateName, "An equipment type with this name already exists.");
        }

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.EquipmentType.Created,
            EntityType: "EquipmentType",
            EntityId: type.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { type.Name, Kind = type.Kind.ToString(), type.AssetTagPrefix }), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(type));
    }

    public async Task<ServiceResult<EquipmentTypeRecord>> UpdateAsync(
        Guid id, UpdateEquipmentTypeData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var type = await dbContext.EquipmentTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (type is null)
        {
            return ServiceResult.Fail<EquipmentTypeRecord>(ServiceErrorCode.NotFound, "Equipment type not found.");
        }

        if (!EquipmentTypeRules.IsValidName(data.Name))
        {
            return ServiceResult.Fail<EquipmentTypeRecord>(ServiceErrorCode.Validation, "Name is required.");
        }

        var normalizedPrefix = EquipmentTypeRules.NormalizePrefix(data.AssetTagPrefix);
        if (!EquipmentTypeRules.IsValidPrefix(normalizedPrefix))
        {
            return ServiceResult.Fail<EquipmentTypeRecord>(
                ServiceErrorCode.Validation, "AssetTagPrefix must be 2-6 letters (e.g. \"LT\").");
        }

        var name = data.Name.Trim();
        var normalizedName = EquipmentTypeRules.NormalizeName(name);

        if (normalizedName != type.NormalizedName &&
            await dbContext.EquipmentTypes.AnyAsync(t => t.NormalizedName == normalizedName && t.Id != id, cancellationToken))
        {
            return ServiceResult.Fail<EquipmentTypeRecord>(ServiceErrorCode.DuplicateName, "An equipment type with this name already exists.");
        }

        var oldName = type.Name;
        var oldPrefix = type.AssetTagPrefix;
        type.Name = name;
        type.NormalizedName = normalizedName;
        type.AssetTagPrefix = normalizedPrefix;
        type.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            return ServiceResult.Fail<EquipmentTypeRecord>(ServiceErrorCode.DuplicateName, "An equipment type with this name already exists.");
        }

        if (oldName != type.Name || oldPrefix != type.AssetTagPrefix)
        {
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentType.Updated,
                EntityType: "EquipmentType",
                EntityId: type.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                OldValues: new { Name = oldName, AssetTagPrefix = oldPrefix },
                NewValues: new { type.Name, type.AssetTagPrefix }), cancellationToken);
        }

        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(type));
    }

    public async Task<ServiceResult<EquipmentTypeRecord>> SetActiveAsync(
        Guid id, bool active, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var type = await dbContext.EquipmentTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (type is null)
        {
            return ServiceResult.Fail<EquipmentTypeRecord>(ServiceErrorCode.NotFound, "Equipment type not found.");
        }

        if (type.IsActive == active)
        {
            return ServiceResult.Ok(ToRecord(type));
        }

        // Deactivating a type never touches existing equipment referencing it
        // (Phase 3 scope) — only new
        // equipment registration is blocked (see EquipmentService).
        type.IsActive = active;
        type.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            active ? AuditActions.EquipmentType.Activated : AuditActions.EquipmentType.Deactivated,
            EntityType: "EquipmentType",
            EntityId: type.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(type));
    }

    private static EquipmentTypeRecord ToRecord(EquipmentType type) =>
        new(type.Id, type.Name, type.Kind, type.AssetTagPrefix, type.IsActive, type.CreatedAtUtc, type.UpdatedAtUtc);
}
