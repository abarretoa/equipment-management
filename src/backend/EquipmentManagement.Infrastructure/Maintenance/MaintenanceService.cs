using System.Data;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Damages;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Maintenance;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EquipmentManagement.Infrastructure.Maintenance;

/// <summary>
/// The maintenance workflow (docs/PROJECT_SPEC.md, section 13; Phase 5 scope). Deliberately minimal — no vendor/cost/quote management.
/// <see cref="StartAsync"/> and <see cref="CompleteAsync"/> are the only two
/// methods that can ever change <see cref="MaintenanceRecord.Status"/> or move
/// equipment into/out of <see cref="EquipmentStatus.UnderMaintenance"/>.
/// </summary>
public sealed class MaintenanceService(ApplicationDbContext dbContext, AuditService auditService, TimeProvider timeProvider)
{
    public async Task<PagedResult<MaintenanceSummaryRecord>> ListAsync(
        MaintenanceListFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query =
            from m in dbContext.MaintenanceRecords.AsNoTracking()
            join e in dbContext.Equipment.AsNoTracking() on m.EquipmentId equals e.Id
            join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
            select new { Maintenance = m, AssetTag = e.AssetTag, TypeName = t.Name, e.Manufacturer, e.Model };

        if (filter.EquipmentId is { } equipmentId)
        {
            query = query.Where(x => x.Maintenance.EquipmentId == equipmentId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Maintenance.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(x => x.Maintenance.SentAtUtc)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .Select(x => new MaintenanceSummaryRecord(
                x.Maintenance.Id, x.Maintenance.EquipmentId, x.AssetTag, x.TypeName, x.Manufacturer, x.Model,
                x.Maintenance.Status, x.Maintenance.SentAtUtc, x.Maintenance.CompletedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<MaintenanceSummaryRecord>(items, page1Based, boundedPageSize, totalCount);
    }

    public async Task<MaintenanceDetailRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await (
                from m in dbContext.MaintenanceRecords.AsNoTracking()
                join e in dbContext.Equipment.AsNoTracking() on m.EquipmentId equals e.Id
                join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
                where m.Id == id
                select new { Maintenance = m, AssetTag = e.AssetTag, TypeName = t.Name })
            .FirstOrDefaultAsync(cancellationToken);

        return result is null ? null : ToDetailRecord(result.Maintenance, result.AssetTag, result.TypeName);
    }

    /// <summary>
    /// Only <c>Damaged</c> equipment may enter maintenance (docs/PROJECT_SPEC.md,
    /// section 9). The equipment's currently-<see cref="DamageStatus.Open"/>
    /// damage record, if one exists, is linked automatically — nothing in this
    /// codebase currently produces a <see cref="EquipmentStatus.Damaged"/>
    /// equipment without one, but the field stays optional rather than
    /// assuming that will always hold (see <see cref="MaintenanceRecord.DamageId"/>'s remarks).
    /// </summary>
    public async Task<ServiceResult<MaintenanceDetailRecord>> StartAsync(
        Guid equipmentId, StartMaintenanceData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        // Loaded after opening the Serializable transaction, same rationale as
        // EquipmentReturnService.ReceiveAsync/FinalizeInspectionAsync — the
        // read that decides "is this equipment still eligible" must be inside
        // the serializable snapshot. The partial unique index on
        // MaintenanceRecords(EquipmentId) WHERE Status = 'Active' is an
        // additional backstop for the fresh-insert path specifically (same
        // belt-and-suspenders pattern as EquipmentAssignment, see ADR-031).
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var equipment = await dbContext.Equipment.FirstOrDefaultAsync(e => e.Id == equipmentId, cancellationToken);
        if (equipment is null)
        {
            return ServiceResult.Fail<MaintenanceDetailRecord>(ServiceErrorCode.NotFound, "Equipment not found.");
        }

        if (!MaintenanceRules.CanStart(equipment.Status))
        {
            return ServiceResult.Fail<MaintenanceDetailRecord>(
                ServiceErrorCode.MaintenanceNotEligible, $"Equipment in status '{equipment.Status}' cannot enter maintenance.");
        }

        var alreadyActive = await dbContext.MaintenanceRecords.AnyAsync(m => m.EquipmentId == equipmentId && m.Status == MaintenanceStatus.Active, cancellationToken);
        if (alreadyActive)
        {
            return ServiceResult.Fail<MaintenanceDetailRecord>(ServiceErrorCode.MaintenanceNotEligible, "This equipment already has an active maintenance record.");
        }

        var openDamage = await dbContext.Damages
            .Where(d => d.EquipmentId == equipmentId && d.Status == DamageStatus.Open)
            .OrderByDescending(d => d.ReportedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var maintenance = new MaintenanceRecord
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            DamageId = openDamage?.Id,
            Status = MaintenanceStatus.Active,
            Notes = Trim(data.Notes),
            SentAtUtc = now,
            CreatedByUserId = actorUserId,
            CreatedByDisplayNameSnapshot = actorDisplayName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        equipment.Status = EquipmentStatus.UnderMaintenance;
        equipment.UpdatedAtUtc = now;

        dbContext.MaintenanceRecords.Add(maintenance);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.Maintenance.Started,
                EntityType: "Equipment",
                EntityId: equipment.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { equipment.AssetTag, MaintenanceId = maintenance.Id }), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.Find(ex, PostgresErrorCodes.UniqueViolation, PostgresErrorCodes.SerializationFailure) is not null)
        {
            return ServiceResult.Fail<MaintenanceDetailRecord>(
                ServiceErrorCode.Concurrency, "Another operation already started maintenance for this equipment; please retry.");
        }

        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);
        return ServiceResult.Ok(ToDetailRecord(maintenance, equipment.AssetTag, type.Name));
    }

    /// <summary>
    /// <c>Repaired</c> returns equipment to circulation and resolves the
    /// linked damage, if any; <c>NotRepairable</c> sends equipment back to
    /// <c>Damaged</c> for a separate, explicit Administration retirement
    /// decision (Phase 5 scope) —
    /// this method never retires equipment itself.
    /// </summary>
    public async Task<ServiceResult<MaintenanceDetailRecord>> CompleteAsync(
        Guid maintenanceId, CompleteMaintenanceData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        // Same rationale as StartAsync — loaded inside the Serializable
        // transaction so a genuine concurrent double-completion is detected
        // rather than silently lost-updated (no fresh-insert backstop exists
        // for this transition).
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var maintenance = await dbContext.MaintenanceRecords.FirstOrDefaultAsync(m => m.Id == maintenanceId, cancellationToken);
        if (maintenance is null)
        {
            return ServiceResult.Fail<MaintenanceDetailRecord>(ServiceErrorCode.NotFound, "Maintenance record not found.");
        }

        if (!MaintenanceRules.CanComplete(maintenance.Status))
        {
            return ServiceResult.Fail<MaintenanceDetailRecord>(ServiceErrorCode.MaintenanceNotActive, "This maintenance record is not active.");
        }

        var equipment = await dbContext.Equipment.FirstAsync(e => e.Id == maintenance.EquipmentId, cancellationToken);
        if (equipment.Status != EquipmentStatus.UnderMaintenance)
        {
            return ServiceResult.Fail<MaintenanceDetailRecord>(
                ServiceErrorCode.InvalidLifecycleTransition, $"Equipment in status '{equipment.Status}' cannot complete maintenance.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        maintenance.Status = MaintenanceStatus.Completed;
        maintenance.Outcome = data.Outcome;
        maintenance.Notes = Trim(data.Notes) ?? maintenance.Notes;
        maintenance.CompletedAtUtc = now;
        maintenance.UpdatedAtUtc = now;

        equipment.Status = MaintenanceRules.ResultingEquipmentStatus(data.Outcome);
        equipment.UpdatedAtUtc = now;

        Damage? resolvedDamage = null;
        if (data.Outcome == MaintenanceOutcome.Repaired && maintenance.DamageId is { } damageId)
        {
            resolvedDamage = await dbContext.Damages.FirstOrDefaultAsync(d => d.Id == damageId, cancellationToken);
            if (resolvedDamage is not null && resolvedDamage.Status == DamageStatus.Open)
            {
                resolvedDamage.Status = DamageStatus.Resolved;
                resolvedDamage.ResolvedAtUtc = now;
                resolvedDamage.UpdatedAtUtc = now;
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.Maintenance.Completed,
                EntityType: "Equipment",
                EntityId: equipment.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { equipment.AssetTag, Outcome = data.Outcome.ToString(), ResultingStatus = equipment.Status.ToString() }), cancellationToken);

            if (resolvedDamage is not null && resolvedDamage.Status == DamageStatus.Resolved)
            {
                await auditService.RecordAsync(new AuditEntry(
                    AuditActions.Damage.Resolved,
                    EntityType: "Equipment",
                    EntityId: equipment.Id.ToString(),
                    ActorUserId: actorUserId,
                    ActorDisplayNameSnapshot: actorDisplayName,
                    Metadata: new { DamageId = resolvedDamage.Id, equipment.AssetTag }), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsSerializationFailure(ex))
        {
            return ServiceResult.Fail<MaintenanceDetailRecord>(
                ServiceErrorCode.Concurrency, "This maintenance record was already completed by another operation; please retry.");
        }

        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);
        return ServiceResult.Ok(ToDetailRecord(maintenance, equipment.AssetTag, type.Name));
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MaintenanceDetailRecord ToDetailRecord(MaintenanceRecord maintenance, string assetTag, string equipmentTypeName) => new(
        maintenance.Id, maintenance.EquipmentId, assetTag, equipmentTypeName, maintenance.DamageId, maintenance.Status,
        maintenance.Notes, maintenance.Outcome, maintenance.SentAtUtc, maintenance.CompletedAtUtc, maintenance.CreatedAtUtc, maintenance.UpdatedAtUtc);
}
