using System.Data;
using System.Security.Cryptography;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Files;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Files;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EquipmentManagement.Infrastructure.Handovers;

/// <summary>
/// Handover/assignment/delivery-inspection workflow (docs/PROJECT_SPEC.md,
/// sections 9-11, 19-21). <see cref="FinalizeAsync"/> is the only method that
/// can ever create an <see cref="EquipmentAssignment"/> or move
/// <see cref="Equipment.Status"/> to <see cref="EquipmentStatus.Assigned"/> —
/// nothing here can rewrite a finalized handover's items, inspections or
/// snapshots (see ADR-030).
/// </summary>
public sealed class EquipmentHandoverService(
    ApplicationDbContext dbContext,
    AuditService auditService,
    IPrivateFileStorage fileStorage,
    IOptions<PrivateStorageOptions> storageOptions,
    TimeProvider timeProvider,
    ILogger<EquipmentHandoverService> logger)
{
    public async Task<PagedResult<EquipmentHandoverSummaryRecord>> ListAsync(
        EquipmentHandoverListFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query =
            from h in dbContext.EquipmentHandovers.AsNoTracking()
            join c in dbContext.Consultants.AsNoTracking() on h.ConsultantId equals c.Id
            select new { Handover = h, ConsultantFullName = c.FullName };

        if (filter.ConsultantId is { } consultantId)
        {
            query = query.Where(x => x.Handover.ConsultantId == consultantId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Handover.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(x => EF.Functions.ILike(x.ConsultantFullName, $"%{filter.Search}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var page1 = await query
            .OrderByDescending(x => x.Handover.CreatedAtUtc)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .Select(x => new { x.Handover.Id, x.Handover.ConsultantId, x.ConsultantFullName, x.Handover.Status, x.Handover.DeliveredOn, x.Handover.CreatedAtUtc, x.Handover.FinalizedAtUtc })
            .ToListAsync(cancellationToken);

        var handoverIds = page1.Select(x => x.Id).ToList();
        var itemCounts = await dbContext.EquipmentHandoverItems.AsNoTracking()
            .Where(i => handoverIds.Contains(i.EquipmentHandoverId))
            .GroupBy(i => i.EquipmentHandoverId)
            .Select(g => new { HandoverId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.HandoverId, x => x.Count, cancellationToken);

        // One batched join for the whole page (never one query per row): a
        // handover has a signed current term exactly when its Generated
        // ResponsibilityTerm has a matching ResponsibilityTermSignedArtifact.
        var signedCurrentTermHandoverIds = (await (
                from t in dbContext.ResponsibilityTerms.AsNoTracking()
                join a in dbContext.ResponsibilityTermSignedArtifacts.AsNoTracking() on t.Id equals a.ResponsibilityTermId
                where handoverIds.Contains(t.EquipmentHandoverId) && t.Status == ResponsibilityTermStatus.Generated
                select t.EquipmentHandoverId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var items = page1
            .Select(x => new EquipmentHandoverSummaryRecord(
                x.Id, x.ConsultantId, x.ConsultantFullName, x.Status,
                HandoverOperationalStatusRules.Derive(x.Status, signedCurrentTermHandoverIds.Contains(x.Id)),
                x.DeliveredOn, itemCounts.GetValueOrDefault(x.Id), x.CreatedAtUtc, x.FinalizedAtUtc))
            .ToList();

        return new PagedResult<EquipmentHandoverSummaryRecord>(items, page1Based, boundedPageSize, totalCount);
    }

    public async Task<EquipmentHandoverDetailRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var handover = await dbContext.EquipmentHandovers.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (handover is null)
        {
            return null;
        }

        var consultant = await dbContext.Consultants.AsNoTracking().FirstAsync(c => c.Id == handover.ConsultantId, cancellationToken);
        var items = await BuildItemRecordsAsync(id, cancellationToken);
        var operationalStatus = await DeriveOperationalStatusAsync(id, handover.Status, cancellationToken);
        var dispatch = await dbContext.EquipmentDispatches.AsNoTracking().Active().FirstOrDefaultAsync(d => d.EquipmentHandoverId == id, cancellationToken);
        var dispatchRecord = dispatch is null
            ? null
            : new EquipmentDispatchRecord(dispatch.Id, dispatch.EquipmentHandoverId, dispatch.ExpectedDeliveryFromDate, dispatch.ExpectedDeliveryToDate, dispatch.DispatchedAtUtc, dispatch.DispatchedByDisplayNameSnapshot);
        var cancelledDispatches = await dbContext.EquipmentDispatches.AsNoTracking()
            .Where(d => d.EquipmentHandoverId == id && d.CancelledAtUtc != null)
            .OrderByDescending(d => d.CancelledAtUtc)
            .Select(d => new CancelledDispatchRecord(
                d.Id, d.DispatchedAtUtc, d.DispatchedByDisplayNameSnapshot, d.CancelledAtUtc!.Value, d.CancelledByDisplayNameSnapshot!, d.CancellationReason!))
            .ToListAsync(cancellationToken);

        return ToDetailRecord(handover, consultant.FullName, items, operationalStatus, dispatchRecord) with { CancelledDispatches = cancelledDispatches };
    }

    public async Task<ServiceResult<EquipmentHandoverDetailRecord>> CreateAsync(
        CreateHandoverData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var consultant = await dbContext.Consultants.AsNoTracking().FirstOrDefaultAsync(c => c.Id == data.ConsultantId, cancellationToken);
        if (consultant is null)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.NotFound, "Consultant not found.");
        }

        if (!consultant.IsActive)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.InactiveReference, "Um consultor inativo não pode receber uma nova entrega.");
        }

        if (!Enum.IsDefined(data.DeliveryMethod))
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.Validation, "Selecione o método de entrega.");
        }

        var eligibility = await HandoverConsultantEligibility.ResolveAsync(dbContext, logger, consultant.Id, cancellationToken);
        if (eligibility != ConsultantHandoverEligibility.Eligible)
        {
            var (code, message) = HandoverConsultantEligibility.ToFailure(eligibility, HandoverConsultantEligibility.CreateMessage);
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(code, message);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var handover = new EquipmentHandover
        {
            Id = Guid.NewGuid(),
            ConsultantId = consultant.Id,
            Status = HandoverStatus.Draft,
            DeliveryMethod = data.DeliveryMethod,
            Notes = Trim(data.Notes),
            CreatedByUserId = actorUserId,
            CreatedByDisplayNameSnapshot = actorDisplayName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        dbContext.EquipmentHandovers.Add(handover);

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.EquipmentHandover.Created,
            EntityType: "EquipmentHandover",
            EntityId: handover.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { ConsultantId = consultant.Id, DeliveryMethod = data.DeliveryMethod.ToString() }), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToDetailRecord(handover, consultant.FullName, [], HandoverOperationalStatus.Draft));
    }

    /// <summary>
    /// Chooses or changes how the equipment reaches the Consultant (Iteration
    /// 3). Also the only way a legacy Draft with no recorded
    /// method gets one. Locked by the same rule as the item composition once
    /// a dispatch exists (<see cref="HandoverRules.CanChangeDeliveryMethod"/>).
    /// </summary>
    public async Task<ServiceResult<EquipmentHandoverDetailRecord>> SetDeliveryMethodAsync(
        Guid handoverId, HandoverDeliveryMethod deliveryMethod, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var handover = await dbContext.EquipmentHandovers.FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
        if (handover is null)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.NotFound, "Handover not found.");
        }

        if (!HandoverRules.CanModify(handover.Status))
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.HandoverNotDraft, "Uma entrega confirmada não pode ser alterada.");
        }

        if (!Enum.IsDefined(deliveryMethod))
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.Validation, "Selecione o método de entrega.");
        }

        var dispatch = await dbContext.EquipmentDispatches.AsNoTracking().Active().FirstOrDefaultAsync(d => d.EquipmentHandoverId == handoverId, cancellationToken);
        if (!HandoverRules.CanChangeDeliveryMethod(handover.Status, dispatch is not null))
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.HandoverCompositionLocked, "O envio desta entrega já foi registado; o método de entrega não pode ser alterado.");
        }

        var previous = handover.DeliveryMethod;
        if (previous != deliveryMethod)
        {
            handover.DeliveryMethod = deliveryMethod;
            handover.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

            await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentHandover.DeliveryMethodChanged,
                EntityType: "EquipmentHandover",
                EntityId: handoverId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                OldValues: new { DeliveryMethod = previous?.ToString() },
                NewValues: new { DeliveryMethod = deliveryMethod.ToString() }), cancellationToken);
            await transaction.CommitIfOwnedAsync(cancellationToken);
        }

        var consultant = await dbContext.Consultants.AsNoTracking().FirstAsync(c => c.Id == handover.ConsultantId, cancellationToken);
        var items = await BuildItemRecordsAsync(handoverId, cancellationToken);
        return ServiceResult.Ok(ToDetailRecord(handover, consultant.FullName, items, HandoverOperationalStatus.Draft));
    }

    public async Task<ServiceResult<EquipmentHandoverDetailRecord>> UpdateHeaderAsync(
        Guid handoverId, UpdateHandoverHeaderData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var handover = await dbContext.EquipmentHandovers.FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
        if (handover is null)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.NotFound, "Handover not found.");
        }

        if (!HandoverRules.CanModify(handover.Status))
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.HandoverNotDraft, "A finalized handover cannot be modified.");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (data.DeliveredOn is { } deliveredOn && deliveredOn > today)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.Validation, "Delivery date cannot be in the future.");
        }

        handover.DeliveredOn = data.DeliveredOn;
        handover.Notes = Trim(data.Notes);
        handover.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);

        var consultant = await dbContext.Consultants.AsNoTracking().FirstAsync(c => c.Id == handover.ConsultantId, cancellationToken);
        var items = await BuildItemRecordsAsync(handoverId, cancellationToken);
        return ServiceResult.Ok(ToDetailRecord(handover, consultant.FullName, items, HandoverOperationalStatus.Draft));
    }

    public async Task<ServiceResult<EquipmentHandoverDetailRecord>> AddItemAsync(
        Guid handoverId, Guid equipmentId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var handover = await dbContext.EquipmentHandovers.FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
        if (handover is null)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.NotFound, "Handover not found.");
        }

        if (!HandoverRules.CanModify(handover.Status))
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.HandoverNotDraft, "A finalized handover cannot be modified.");
        }

        var hasDispatch = await dbContext.EquipmentDispatches.AsNoTracking().Active().AnyAsync(d => d.EquipmentHandoverId == handoverId, cancellationToken);
        if (!HandoverRules.CanModifyComposition(handover.Status, hasDispatch))
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.HandoverCompositionLocked, "Este Handover já tem um envio registado; a composição não pode ser alterada.");
        }

        var equipment = await dbContext.Equipment.AsNoTracking().FirstOrDefaultAsync(e => e.Id == equipmentId, cancellationToken);
        if (equipment is null)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.NotFound, "Equipment not found.");
        }

        if (!EquipmentLifecycleRules.CanAssign(equipment.Status))
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.InvalidLifecycleTransition, $"Equipment in status '{equipment.Status}' cannot be handed over.");
        }

        var alreadyInThisHandover = await dbContext.EquipmentHandoverItems.AsNoTracking()
            .AnyAsync(i => i.EquipmentHandoverId == handoverId && i.EquipmentId == equipmentId, cancellationToken);
        if (alreadyInThisHandover)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.EquipmentAlreadyInHandover, "This equipment is already part of this handover.");
        }

        var item = new EquipmentHandoverItem
        {
            Id = Guid.NewGuid(),
            EquipmentHandoverId = handoverId,
            EquipmentId = equipmentId,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };

        dbContext.EquipmentHandoverItems.Add(item);

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.EquipmentHandover.ItemAdded,
            EntityType: "EquipmentHandover",
            EntityId: handoverId.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { equipment.AssetTag }), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        var consultant = await dbContext.Consultants.AsNoTracking().FirstAsync(c => c.Id == handover.ConsultantId, cancellationToken);
        var items = await BuildItemRecordsAsync(handoverId, cancellationToken);
        return ServiceResult.Ok(ToDetailRecord(handover, consultant.FullName, items, HandoverOperationalStatus.Draft));
    }

    public async Task<ServiceResult> RemoveItemAsync(
        Guid handoverId, Guid itemId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var handover = await dbContext.EquipmentHandovers.FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
        if (handover is null)
        {
            return ServiceResult.Fail(ServiceErrorCode.NotFound, "Handover not found.");
        }

        if (!HandoverRules.CanModify(handover.Status))
        {
            return ServiceResult.Fail(ServiceErrorCode.HandoverNotDraft, "A finalized handover cannot be modified.");
        }

        var hasDispatch = await dbContext.EquipmentDispatches.AsNoTracking().Active().AnyAsync(d => d.EquipmentHandoverId == handoverId, cancellationToken);
        if (!HandoverRules.CanModifyComposition(handover.Status, hasDispatch))
        {
            return ServiceResult.Fail(ServiceErrorCode.HandoverCompositionLocked, "Este Handover já tem um envio registado; a composição não pode ser alterada.");
        }

        var item = await dbContext.EquipmentHandoverItems.FirstOrDefaultAsync(i => i.Id == itemId && i.EquipmentHandoverId == handoverId, cancellationToken);
        if (item is null)
        {
            return ServiceResult.Fail(ServiceErrorCode.NotFound, "Item not found.");
        }

        var equipment = await dbContext.Equipment.AsNoTracking().FirstAsync(e => e.Id == item.EquipmentId, cancellationToken);

        var photoKeys = await (
            from p in dbContext.DeliveryInspectionPhotos.AsNoTracking()
            join i in dbContext.DeliveryInspections.AsNoTracking() on p.DeliveryInspectionId equals i.Id
            where i.EquipmentHandoverItemId == itemId
            select p.StorageKey).ToListAsync(cancellationToken);

        // Removing the item cascades to its inspection and photo rows at the
        // database level (see ApplicationDbContext) — only the storage objects
        // need explicit best-effort cleanup here.
        dbContext.EquipmentHandoverItems.Remove(item);

        await using (var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken))
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentHandover.ItemRemoved,
                EntityType: "EquipmentHandover",
                EntityId: handoverId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { equipment.AssetTag }), cancellationToken);
            await transaction.CommitIfOwnedAsync(cancellationToken);
        }

        // Only once the removal is committed — never inside the transaction.
        foreach (var key in photoKeys)
        {
            await fileStorage.DeleteAsync(key, cancellationToken);
        }

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Deletes an abandoned Draft and its Draft-only rows (items, delivery
    /// inspections, inspection photos) — see <see cref="HandoverRules.CanDeleteDraft"/>.
    /// Equipment is never touched: a Draft never changed Equipment.Status, so
    /// it stays Available. Eligibility is re-read inside a Serializable
    /// transaction, and the Restrict foreign keys from dispatch, assignment and
    /// Responsibility Term rows are the database backstop: a concurrent
    /// "Registar envio" or "Confirmar entrega" makes this fail closed.
    /// </summary>
    public async Task<ServiceResult> DeleteDraftAsync(
        Guid handoverId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.EquipmentHandovers.AsNoTracking().AnyAsync(h => h.Id == handoverId, cancellationToken))
        {
            return ServiceResult.Fail(ServiceErrorCode.NotFound, "Handover not found.");
        }

        List<string> photoKeys;
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var handover = await dbContext.EquipmentHandovers.FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
            if (handover is null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Handover not found.");
            }

            // Any dispatch row, cancelled ones included: a Draft with dispatch
            // history is never deleted, so that history is never lost.
            var hasDispatch = await dbContext.EquipmentDispatches.AnyAsync(d => d.EquipmentHandoverId == handoverId, cancellationToken);
            var hasAssignment = await dbContext.EquipmentAssignments.AnyAsync(a => a.EquipmentHandoverId == handoverId, cancellationToken);
            var hasTerm = await dbContext.ResponsibilityTerms.AnyAsync(t => t.EquipmentHandoverId == handoverId, cancellationToken);
            if (!HandoverRules.CanDeleteDraft(handover.Status, hasDispatch, hasAssignment, hasTerm))
            {
                return handover.Status != HandoverStatus.Draft
                    ? ServiceResult.Fail(ServiceErrorCode.HandoverNotDraft, "Esta entrega já foi confirmada e não pode ser eliminada.")
                    : hasDispatch
                        ? ServiceResult.Fail(ServiceErrorCode.HandoverCompositionLocked, "Esta entrega já tem um envio registado e não pode ser eliminada.")
                        : ServiceResult.Fail(ServiceErrorCode.HandoverNotDraft, "Esta entrega tem registos operacionais associados e não pode ser eliminada.");
            }

            var items = await dbContext.EquipmentHandoverItems.Where(i => i.EquipmentHandoverId == handoverId).ToListAsync(cancellationToken);
            var itemIds = items.Select(i => i.Id).ToList();
            var assetTags = await dbContext.Equipment.AsNoTracking()
                .Where(e => items.Select(i => i.EquipmentId).Contains(e.Id))
                .OrderBy(e => e.AssetTag)
                .Select(e => e.AssetTag)
                .ToListAsync(cancellationToken);
            photoKeys = await (
                from p in dbContext.DeliveryInspectionPhotos.AsNoTracking()
                join i in dbContext.DeliveryInspections.AsNoTracking() on p.DeliveryInspectionId equals i.Id
                where itemIds.Contains(i.EquipmentHandoverItemId)
                select p.StorageKey).ToListAsync(cancellationToken);

            // Items cascade to their inspection and photo rows at the database
            // level (see ApplicationDbContext); the handover row goes last.
            dbContext.EquipmentHandoverItems.RemoveRange(items);
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.EquipmentHandovers.Remove(handover);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentHandover.DraftDeleted,
                EntityType: "EquipmentHandover",
                EntityId: handoverId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                OldValues: new { handover.ConsultantId, DeliveryMethod = handover.DeliveryMethod?.ToString(), ItemCount = items.Count, AssetTags = assetTags }), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.Find(
            ex, PostgresErrorCodes.SerializationFailure, PostgresErrorCodes.RestrictViolation, PostgresErrorCodes.ForeignKeyViolation) is not null)
        {
            // A concurrent "Registar envio"/"Confirmar entrega" (or a new
            // item) touched this Draft between our reads and the delete:
            // the Serializable snapshot or a Restrict foreign key rejects it.
            dbContext.ChangeTracker.Clear();
            return ServiceResult.Fail(
                ServiceErrorCode.Concurrency, "Esta entrega foi alterada por outra operação. Atualize a página e tente novamente.");
        }

        // Only once the deletion is committed — never inside the transaction.
        foreach (var key in photoKeys)
        {
            await fileStorage.DeleteAsync(key, cancellationToken);
        }

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<DeliveryInspectionRecord>> RecordInspectionAsync(
        Guid handoverId, Guid itemId, RecordInspectionData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var handover = await dbContext.EquipmentHandovers.AsNoTracking().FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
        if (handover is null)
        {
            return ServiceResult.Fail<DeliveryInspectionRecord>(ServiceErrorCode.NotFound, "Handover not found.");
        }

        if (!HandoverRules.CanModify(handover.Status))
        {
            return ServiceResult.Fail<DeliveryInspectionRecord>(ServiceErrorCode.HandoverNotDraft, "A finalized handover cannot be modified.");
        }

        var item = await dbContext.EquipmentHandoverItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == itemId && i.EquipmentHandoverId == handoverId, cancellationToken);
        if (item is null)
        {
            return ServiceResult.Fail<DeliveryInspectionRecord>(ServiceErrorCode.NotFound, "Item not found.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var inspection = await dbContext.DeliveryInspections.FirstOrDefaultAsync(i => i.EquipmentHandoverItemId == itemId, cancellationToken);
        if (inspection is null)
        {
            inspection = new DeliveryInspection
            {
                Id = Guid.NewGuid(),
                EquipmentHandoverItemId = itemId,
                Condition = data.Condition,
                Notes = Trim(data.Notes),
                InspectedByUserId = actorUserId,
                InspectedByDisplayNameSnapshot = actorDisplayName,
                InspectedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            dbContext.DeliveryInspections.Add(inspection);
        }
        else
        {
            inspection.Condition = data.Condition;
            inspection.Notes = Trim(data.Notes);
            inspection.InspectedByUserId = actorUserId;
            inspection.InspectedByDisplayNameSnapshot = actorDisplayName;
            inspection.InspectedAtUtc = now;
            inspection.UpdatedAtUtc = now;
        }

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.EquipmentHandover.InspectionRecorded,
            EntityType: "EquipmentHandover",
            EntityId: handoverId.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { ItemId = itemId, Condition = inspection.Condition.ToString() }), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        var photos = await BuildPhotoRecordsAsync(inspection.Id, cancellationToken);
        return ServiceResult.Ok(ToInspectionRecord(inspection, photos));
    }

    public async Task<ServiceResult<DeliveryInspectionPhotoRecord>> UploadPhotoAsync(
        Guid handoverId, Guid itemId, Stream content, string originalFileName, string declaredContentType,
        Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var handover = await dbContext.EquipmentHandovers.AsNoTracking().FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
        if (handover is null)
        {
            return ServiceResult.Fail<DeliveryInspectionPhotoRecord>(ServiceErrorCode.NotFound, "Handover not found.");
        }

        if (!HandoverRules.CanModify(handover.Status))
        {
            return ServiceResult.Fail<DeliveryInspectionPhotoRecord>(ServiceErrorCode.HandoverNotDraft, "A finalized handover cannot be modified.");
        }

        var item = await dbContext.EquipmentHandoverItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == itemId && i.EquipmentHandoverId == handoverId, cancellationToken);
        if (item is null)
        {
            return ServiceResult.Fail<DeliveryInspectionPhotoRecord>(ServiceErrorCode.NotFound, "Item not found.");
        }

        var inspection = await dbContext.DeliveryInspections.FirstOrDefaultAsync(i => i.EquipmentHandoverItemId == itemId, cancellationToken);
        if (inspection is null)
        {
            return ServiceResult.Fail<DeliveryInspectionPhotoRecord>(
                ServiceErrorCode.InspectionMissing, "Record the delivery inspection for this item before uploading photos.");
        }

        var maxSize = storageOptions.Value.MaxPhotoSizeBytes;
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > maxSize)
        {
            return ServiceResult.Fail<DeliveryInspectionPhotoRecord>(
                ServiceErrorCode.FileTooLarge, $"Photo must be between 1 byte and {maxSize} bytes.");
        }

        var bytes = buffer.ToArray();
        var detected = ImageSignatureDetector.Detect(bytes);
        if (detected is null || !ImageSignatureDetector.MatchesDeclaredContentType(detected.Value, declaredContentType))
        {
            return ServiceResult.Fail<DeliveryInspectionPhotoRecord>(
                ServiceErrorCode.InvalidFile, "Only genuine JPEG, PNG or WebP images are accepted.");
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var storageKey = BuildStorageKey(detected.Value);

        buffer.Position = 0;
        await fileStorage.SaveAsync(storageKey, buffer, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var sortOrder = await dbContext.DeliveryInspectionPhotos.CountAsync(p => p.DeliveryInspectionId == inspection.Id, cancellationToken);

        var photo = new DeliveryInspectionPhoto
        {
            Id = Guid.NewGuid(),
            DeliveryInspectionId = inspection.Id,
            StorageKey = storageKey,
            OriginalFileName = Trim(originalFileName) ?? "photo",
            ContentType = declaredContentType,
            SizeBytes = bytes.LongLength,
            Sha256Hash = sha256,
            SortOrder = sortOrder,
            UploadedAtUtc = now,
            UploadedByUserId = actorUserId,
        };

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
            dbContext.DeliveryInspectionPhotos.Add(photo);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentHandover.PhotoUploaded,
                EntityType: "EquipmentHandover",
                EntityId: handoverId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { ItemId = itemId, photo.OriginalFileName, photo.SizeBytes }), cancellationToken);
            await transaction.CommitIfOwnedAsync(cancellationToken);
        }
        catch
        {
            // The DB row (and its audit row) is the source of truth; if it fails
            // to persist, don't leave an orphaned object behind
            // (docs/PROJECT_SPEC.md, section 23).
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
            throw;
        }

        return ServiceResult.Ok(new DeliveryInspectionPhotoRecord(
            photo.Id, photo.OriginalFileName, photo.ContentType, photo.SizeBytes, photo.SortOrder, photo.UploadedAtUtc));
    }

    public async Task<ServiceResult> DeletePhotoAsync(
        Guid handoverId, Guid itemId, Guid photoId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var handover = await dbContext.EquipmentHandovers.AsNoTracking().FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
        if (handover is null)
        {
            return ServiceResult.Fail(ServiceErrorCode.NotFound, "Handover not found.");
        }

        if (!HandoverRules.CanModify(handover.Status))
        {
            return ServiceResult.Fail(ServiceErrorCode.HandoverNotDraft, "A finalized handover cannot be modified.");
        }

        // The complete photo → inspection → item → handover chain must match the
        // route: the editable Draft checked above must own the photo being
        // deleted, never merely some item/photo pair elsewhere (a mismatch is a
        // 404, indistinguishable from a photo that does not exist).
        var photo = await (
            from p in dbContext.DeliveryInspectionPhotos
            join i in dbContext.DeliveryInspections on p.DeliveryInspectionId equals i.Id
            join item in dbContext.EquipmentHandoverItems on i.EquipmentHandoverItemId equals item.Id
            where p.Id == photoId && item.Id == itemId && item.EquipmentHandoverId == handoverId
            select p).FirstOrDefaultAsync(cancellationToken);

        if (photo is null)
        {
            return ServiceResult.Fail(ServiceErrorCode.NotFound, "Photo not found.");
        }

        var storageKey = photo.StorageKey;
        dbContext.DeliveryInspectionPhotos.Remove(photo);

        await using (var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken))
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentHandover.PhotoDeleted,
                EntityType: "EquipmentHandover",
                EntityId: handoverId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { ItemId = itemId, PhotoId = photoId }), cancellationToken);
            await transaction.CommitIfOwnedAsync(cancellationToken);
        }

        // DB row is removed first; an orphaned storage object is the accepted
        // lesser failure mode versus a DB row pointing at nothing.
        await fileStorage.DeleteAsync(storageKey, cancellationToken);

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Validates the full <paramref name="handoverId"/>/<paramref name="itemId"/>/
    /// <paramref name="photoId"/> chain, not just that <paramref name="photoId"/>
    /// exists somewhere — a route claiming a parent relationship should enforce it,
    /// even though every caller here already passes every permission check this
    /// application has (Phase 10 hardening: defense-in-depth, not a fix for a live
    /// privilege bypass — Administration/HR both already see every handover).
    /// </summary>
    public async Task<(Stream Content, string ContentType, string OriginalFileName)?> GetPhotoContentAsync(
        Guid handoverId, Guid itemId, Guid photoId, CancellationToken cancellationToken = default)
    {
        var photo = await (
                from p in dbContext.DeliveryInspectionPhotos.AsNoTracking()
                join inspection in dbContext.DeliveryInspections.AsNoTracking() on p.DeliveryInspectionId equals inspection.Id
                join item in dbContext.EquipmentHandoverItems.AsNoTracking() on inspection.EquipmentHandoverItemId equals item.Id
                where p.Id == photoId && item.Id == itemId && item.EquipmentHandoverId == handoverId
                select p)
            .FirstOrDefaultAsync(cancellationToken);
        if (photo is null)
        {
            return null;
        }

        var stream = await fileStorage.OpenReadAsync(photo.StorageKey, cancellationToken);
        return stream is null ? null : (stream, photo.ContentType, photo.OriginalFileName);
    }

    /// <summary>
    /// The only method that can create an <see cref="EquipmentAssignment"/> or
    /// move equipment to <see cref="EquipmentStatus.Assigned"/>
    /// (docs/PROJECT_SPEC.md, invariant #10: "dentro da mesma transação").
    /// Snapshots are captured here, from live data, exactly once — unless a
    /// carrier Responsibility Term was already prepared after dispatch, whose
    /// generation froze them (they are then kept unchanged).
    /// </summary>
    public async Task<ServiceResult<EquipmentHandoverDetailRecord>> FinalizeAsync(
        Guid handoverId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var handover = await dbContext.EquipmentHandovers.FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
        if (handover is null)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.NotFound, "Handover not found.");
        }

        if (!HandoverRules.CanFinalize(handover.Status))
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.HandoverNotDraft, "Esta entrega já foi confirmada.");
        }

        // Iteration 3: a legacy Draft (created before the delivery
        // method existed) must have one chosen explicitly; it is never guessed.
        if (handover.DeliveryMethod is not { } deliveryMethod)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.DeliveryMethodRequired, "Selecione o método de entrega antes de confirmar a entrega.");
        }

        var consultant = await dbContext.Consultants.AsNoTracking().FirstAsync(c => c.Id == handover.ConsultantId, cancellationToken);
        if (!consultant.IsActive)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.InactiveReference, "Um consultor inativo não pode receber equipamento.");
        }

        // Re-checked here, not only at creation: a Draft may have been created
        // while the Consultant was eligible and every allocation ended since.
        var eligibility = await HandoverConsultantEligibility.ResolveAsync(dbContext, logger, consultant.Id, cancellationToken);
        if (eligibility != ConsultantHandoverEligibility.Eligible)
        {
            var (code, message) = HandoverConsultantEligibility.ToFailure(eligibility);
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(code, message);
        }

        if (HandoverRules.RequiresDispatchBeforeFinalize(deliveryMethod) &&
            !await dbContext.EquipmentDispatches.AsNoTracking().Active().AnyAsync(d => d.EquipmentHandoverId == handoverId, cancellationToken))
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.DispatchRequired,
                "O envio ainda não foi registado. Registe o envio e a previsão de entrega antes de confirmar esta entrega.");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (!HandoverRules.IsValidDeliveredOn(handover.DeliveredOn, today))
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.Validation, "Defina a data de entrega (não pode ser uma data futura) antes de confirmar a entrega.");
        }

        var items = await dbContext.EquipmentHandoverItems.AsNoTracking().Where(i => i.EquipmentHandoverId == handoverId).ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.Validation, "Adicione pelo menos um equipamento antes de confirmar a entrega.");
        }

        // A carrier delivery confirms the equipment it shipped (InTransit); an
        // in-person delivery hands over Available equipment directly.
        var requiredStatus = EquipmentLifecycleRules.RequiredStatusForDeliveryConfirmation(
            HandoverRules.RequiresDispatchBeforeFinalize(deliveryMethod));

        // Re-validate every item against live data — nothing here trusts state
        // checked when the item/inspection was originally added.
        var missingPhotoAssetTags = new List<string>();
        var equipmentByItemId = new Dictionary<Guid, (Equipment Equipment, EquipmentType Type, LaptopSpecification? Spec, MobilePhoneSpecification? MobileSpec, EquipmentPhotoSet? PhotoSet)>();
        foreach (var item in items)
        {
            var equipment = await dbContext.Equipment.FirstOrDefaultAsync(e => e.Id == item.EquipmentId, cancellationToken);
            if (equipment is null)
            {
                return ServiceResult.Fail<EquipmentHandoverDetailRecord>(ServiceErrorCode.NotFound, "Equipamento não encontrado.");
            }

            if (equipment.Status != requiredStatus)
            {
                return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                    ServiceErrorCode.InvalidLifecycleTransition, BuildNotDeliverableMessage(equipment.AssetTag));
            }

            var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);
            var spec = type.Kind == EquipmentTypeKind.Laptop
                ? await dbContext.LaptopSpecifications.AsNoTracking().FirstOrDefaultAsync(s => s.EquipmentId == equipment.Id, cancellationToken)
                : null;
            var mobileSpec = type.Kind == EquipmentTypeKind.MobilePhone
                ? await dbContext.MobilePhoneSpecifications.AsNoTracking().FirstOrDefaultAsync(s => s.EquipmentId == equipment.Id, cancellationToken)
                : null;

            var inspection = await dbContext.DeliveryInspections.AsNoTracking().FirstOrDefaultAsync(i => i.EquipmentHandoverItemId == item.Id, cancellationToken);
            if (inspection is null)
            {
                return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                    ServiceErrorCode.InspectionMissing, $"O equipamento {equipment.AssetTag} não tem a inspeção de entrega registada.");
            }

            EquipmentPhotoSet? photoSet = null;
            if (EquipmentRules.RequiresConditionPhotos(type.Kind))
            {
                photoSet = await dbContext.EquipmentPhotoSets.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.EquipmentId == equipment.Id && s.Status == PhotoSetStatus.Current, cancellationToken);
                if (photoSet is null)
                {
                    // Collected rather than returned immediately, so a handover
                    // with several such items names all of them in one message.
                    missingPhotoAssetTags.Add(equipment.AssetTag);
                }
            }

            equipmentByItemId[item.Id] = (equipment, type, spec, mobileSpec, photoSet);
        }

        if (missingPhotoAssetTags.Count > 0)
        {
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.MissingCurrentPhotoSet, BuildMissingPhotosMessage(missingPhotoAssetTags));
        }

        // Serializable isolation plus the partial unique index on
        // EquipmentAssignment(EquipmentId) WHERE Status = 'Active' together
        // close the race where two concurrent finalizations both see an
        // equipment item as Available (docs/PROJECT_SPEC.md, invariant #1) —
        // the same pattern used for allocation creation in Phase 2.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // The status checks above ran before this transaction. A carrier
        // dispatch creates no Assignment, so the unique index alone cannot
        // stop a dispatch of the same item through another Draft that commits
        // in between. Re-read every item here: a change committed before this
        // snapshot is seen and rejected; one committed after it makes the
        // update below fail with a serialization conflict.
        foreach (var (equipment, _, _, _, _) in equipmentByItemId.Values)
        {
            await dbContext.Entry(equipment).ReloadAsync(cancellationToken);
            if (equipment.Status != requiredStatus)
            {
                return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                    ServiceErrorCode.InvalidLifecycleTransition, BuildNotDeliverableMessage(equipment.AssetTag));
            }
        }

        // The snapshot's professional context is THE Consultant's single
        // Active allocation — never "the one with the latest StartDate". The
        // eligibility check above ran outside this transaction, so re-resolve
        // here and fail closed: 0 means it was ended in between, and more than
        // 1 is inconsistent data that must not be silently resolved by
        // choosing one (at most 2 rows are loaded to tell the cases apart).
        var activeAllocations = await dbContext.ConsultantAllocations.AsNoTracking()
            .Where(a => a.ConsultantId == consultant.Id && a.Status == AllocationStatus.Active)
            .Take(2)
            .ToListAsync(cancellationToken);

        var snapshotEligibility = HandoverConsultantEligibility.Resolve(logger, consultant.Id, activeAllocations.Count);
        if (snapshotEligibility != ConsultantHandoverEligibility.Eligible)
        {
            var (code, message) = HandoverConsultantEligibility.ToFailure(snapshotEligibility);
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(code, message);
        }

        var currentAllocation = activeAllocations[0];

        string? companyName = null;
        if (currentAllocation.CompanyId is { } companyId)
        {
            companyName = await dbContext.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // The Handover's delivery record (ConsultantSnapshot, AllocationSnapshot
        // and each item's EquipmentSnapshot/EquipmentPhotoSetId) is frozen
        // here, once. Carrier delivery: a term may already have been prepared
        // after "Registar envio", and that version owns the snapshot its PDF
        // was rendered from. The delivery record is copied FROM that version,
        // so it is exactly the document the Consultant signs; the term itself
        // is never touched (same version, same snapshot, no automatic v2).
        // Every other handover freezes it from live data, as before. Read
        // inside this serializable transaction: a generation racing this
        // confirmation also writes the handover row, so one of the two fails
        // with a serialization conflict. Only the current (Generated) term
        // counts: a term superseded by "Anular envio" belongs to a cancelled
        // dispatch.
        var preparedTerm = await dbContext.ResponsibilityTerms.AsNoTracking()
            .FirstOrDefaultAsync(t => t.EquipmentHandoverId == handoverId && t.Status == ResponsibilityTermStatus.Generated, cancellationToken);

        Dictionary<Guid, ResponsibilityTermItem>? preparedItems = null;
        if (preparedTerm is not null)
        {
            preparedItems = await dbContext.ResponsibilityTermItems.AsNoTracking()
                .Where(i => i.ResponsibilityTermId == preparedTerm.Id)
                .ToDictionaryAsync(i => i.EquipmentId, cancellationToken);

            if (preparedTerm.ConsultantSnapshot is null || preparedItems.Count != items.Count)
            {
                // Inconsistent data; never silently re-captured from live values.
                return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                    ServiceErrorCode.SnapshotIncomplete, "O termo preparado para esta entrega não tem os dados dos equipamentos registados.");
            }

            handover.ConsultantSnapshot = HandoverSnapshotFactory.Copy(preparedTerm.ConsultantSnapshot);
            handover.AllocationSnapshot = HandoverSnapshotFactory.Copy(preparedTerm.AllocationSnapshot);
        }
        else
        {
            handover.ConsultantSnapshot = HandoverSnapshotFactory.ForConsultant(consultant);
            handover.AllocationSnapshot = HandoverSnapshotFactory.ForAllocation(currentAllocation, companyName);
        }

        handover.Status = HandoverStatus.Finalized;
        handover.FinalizedAtUtc = now;
        handover.UpdatedAtUtc = now;

        foreach (var item in items)
        {
            var (equipment, type, spec, mobileSpec, photoSet) = equipmentByItemId[item.Id];

            var trackedItem = await dbContext.EquipmentHandoverItems.FirstAsync(i => i.Id == item.Id, cancellationToken);
            if (preparedItems is not null)
            {
                // Each item exactly as the prepared version froze it; one the
                // version does not cover is inconsistent data, never
                // re-captured from live values.
                if (!preparedItems.TryGetValue(equipment.Id, out var preparedItem))
                {
                    return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                        ServiceErrorCode.SnapshotIncomplete, "O termo preparado para esta entrega não tem os dados dos equipamentos registados.");
                }

                trackedItem.EquipmentSnapshot = HandoverSnapshotFactory.Copy(preparedItem.EquipmentSnapshot);
                trackedItem.EquipmentPhotoSetId = preparedItem.EquipmentPhotoSetId;
            }
            else
            {
                trackedItem.EquipmentSnapshot = HandoverSnapshotFactory.ForEquipment(equipment, type, spec, mobileSpec);
                // Frozen forever at this exact moment — never re-derived from
                // whichever photo set happens to be Current later (see ADR-079).
                trackedItem.EquipmentPhotoSetId = photoSet?.Id;
            }

            equipment.Status = EquipmentStatus.Assigned;
            equipment.UpdatedAtUtc = now;

            dbContext.EquipmentAssignments.Add(new EquipmentAssignment
            {
                Id = Guid.NewGuid(),
                EquipmentId = equipment.Id,
                ConsultantId = consultant.Id,
                Origin = EquipmentAssignmentOrigin.Handover,
                EquipmentHandoverId = handover.Id,
                EquipmentHandoverItemId = item.Id,
                Status = AssignmentStatus.Active,
                StartedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentHandover.Finalized,
                EntityType: "EquipmentHandover",
                EntityId: handover.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new
                {
                    ConsultantId = consultant.Id,
                    AssetTags = equipmentByItemId.Values.Select(v => v.Equipment.AssetTag).ToList(),
                    EquipmentStatusFrom = requiredStatus.ToString(),
                    EquipmentStatusTo = nameof(EquipmentStatus.Assigned),
                }), cancellationToken);

            foreach (var (equipment, _, _, _, _) in equipmentByItemId.Values)
            {
                await auditService.RecordAsync(new AuditEntry(
                    AuditActions.EquipmentAssignment.Started,
                    EntityType: "Equipment",
                    EntityId: equipment.Id.ToString(),
                    ActorUserId: actorUserId,
                    ActorDisplayNameSnapshot: actorDisplayName,
                    NewValues: new { equipment.AssetTag, ConsultantId = consultant.Id, HandoverId = handover.Id }), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.Find(
            ex, PostgresErrorCodes.UniqueViolation, PostgresErrorCodes.SerializationFailure) is not null)
        {
            // Either the partial unique index rejects the EquipmentAssignment
            // insert, or Postgres detects the write-write conflict on the
            // Equipment row under Serializable isolation. The exact wrapper
            // shape varies (raw PostgresException at CommitAsync; DbUpdateException
            // during SaveChangesAsync; EF's execution-strategy additionally wraps
            // that in InvalidOperationException — "likely due to a transient
            // failure" — because a user-managed transaction is active), so
            // PostgresConflictClassifier walks the full InnerException chain
            // instead of matching one shape.
            return ServiceResult.Fail<EquipmentHandoverDetailRecord>(
                ServiceErrorCode.Concurrency, "Um dos equipamentos desta entrega foi atribuído ou enviado por outra operação. Atualize a página e tente novamente.");
        }

        var resultItems = await BuildItemRecordsAsync(handoverId, cancellationToken);
        // Finalization just happened inside this same call — a term prepared
        // before delivery may exist, but never a signed one (signed upload
        // requires a Finalized handover), so this is always
        // AwaitingSignedTerm without needing a query.
        return ServiceResult.Ok(ToDetailRecord(handover, consultant.FullName, resultItems, HandoverOperationalStatus.AwaitingSignedTerm));
    }

    private async Task<IReadOnlyList<EquipmentHandoverItemRecord>> BuildItemRecordsAsync(Guid handoverId, CancellationToken cancellationToken)
    {
        var rows = await (
            from i in dbContext.EquipmentHandoverItems.AsNoTracking()
            join e in dbContext.Equipment.AsNoTracking() on i.EquipmentId equals e.Id
            join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
            where i.EquipmentHandoverId == handoverId
            orderby i.CreatedAtUtc
            select new { Item = i, Equipment = e, TypeName = t.Name }).ToListAsync(cancellationToken);

        var result = new List<EquipmentHandoverItemRecord>(rows.Count);
        foreach (var row in rows)
        {
            var inspection = await dbContext.DeliveryInspections.AsNoTracking().FirstOrDefaultAsync(x => x.EquipmentHandoverItemId == row.Item.Id, cancellationToken);
            DeliveryInspectionRecord? inspectionRecord = null;
            if (inspection is not null)
            {
                var photos = await BuildPhotoRecordsAsync(inspection.Id, cancellationToken);
                inspectionRecord = ToInspectionRecord(inspection, photos);
            }

            var snapshot = row.Item.EquipmentSnapshot is { } s
                ? new EquipmentSnapshotRecord(s.AssetTag, s.EquipmentTypeName, s.Manufacturer, s.Model, s.SerialNumber, s.Processor, s.Memory, s.Storage)
                : null;

            result.Add(new EquipmentHandoverItemRecord(
                row.Item.Id, row.Equipment.Id, row.Equipment.AssetTag, row.TypeName, row.Equipment.Manufacturer,
                row.Equipment.Model, row.Equipment.SerialNumber, snapshot, inspectionRecord, row.Equipment.Status));
        }

        return result;
    }

    private async Task<IReadOnlyList<DeliveryInspectionPhotoRecord>> BuildPhotoRecordsAsync(Guid inspectionId, CancellationToken cancellationToken) =>
        await dbContext.DeliveryInspectionPhotos.AsNoTracking()
            .Where(p => p.DeliveryInspectionId == inspectionId)
            .OrderBy(p => p.SortOrder)
            .Select(p => new DeliveryInspectionPhotoRecord(p.Id, p.OriginalFileName, p.ContentType, p.SizeBytes, p.SortOrder, p.UploadedAtUtc))
            .ToListAsync(cancellationToken);

    private static DeliveryInspectionRecord ToInspectionRecord(DeliveryInspection inspection, IReadOnlyList<DeliveryInspectionPhotoRecord> photos) => new(
        inspection.Id, inspection.Condition, inspection.Notes, inspection.InspectedByDisplayNameSnapshot, inspection.InspectedAtUtc, photos);

    /// <summary>
    /// <paramref name="operationalStatus"/> is supplied by the caller rather
    /// than derived here: every caller except <see cref="GetByIdAsync"/> and
    /// <see cref="FinalizeAsync"/> is guarded by <c>HandoverRules.CanModify</c>
    /// (Draft-only), so the trivial <see cref="HandoverOperationalStatus.Draft"/>
    /// needs no query; <see cref="FinalizeAsync"/> knows its result is
    /// freshly Finalized with no term generated yet, so it can pass
    /// <see cref="HandoverOperationalStatus.AwaitingSignedTerm"/> directly —
    /// only <see cref="GetByIdAsync"/> (and <see cref="ListAsync"/>, which
    /// builds <see cref="EquipmentHandoverSummaryRecord"/> separately) needs
    /// the real Responsibility Term/signature lookup.
    /// </summary>
    private static EquipmentHandoverDetailRecord ToDetailRecord(
        EquipmentHandover handover, string consultantFullName, IReadOnlyList<EquipmentHandoverItemRecord> items,
        HandoverOperationalStatus operationalStatus, EquipmentDispatchRecord? dispatch = null)
    {
        var consultantSnapshot = handover.ConsultantSnapshot is { } cs
            ? new ConsultantSnapshotRecord(cs.FullName, cs.Nif, cs.Address, cs.Phone, cs.Email)
            : null;
        var allocationSnapshot = handover.AllocationSnapshot is { } als
            ? new AllocationSnapshotRecord(als.WorkforceType, als.EngagementType, als.RoleOrFunction, als.CompanyName)
            : null;

        return new EquipmentHandoverDetailRecord(
            handover.Id, handover.ConsultantId, consultantFullName, handover.Status, operationalStatus, handover.DeliveryMethod,
            handover.DeliveredOn, handover.Notes,
            handover.CreatedByUserId, handover.CreatedByDisplayNameSnapshot, handover.CreatedAtUtc, handover.UpdatedAtUtc,
            handover.FinalizedAtUtc, consultantSnapshot, allocationSnapshot, items, dispatch);
    }

    /// <summary>
    /// The one place that looks up whether a handover's current (Generated)
    /// Responsibility Term has a signed artifact — see
    /// <see cref="HandoverOperationalStatusRules"/> for the exact rule this
    /// feeds. Two small indexed lookups (current term id, then its signed
    /// artifact), never N+1 across a list — <see cref="ListAsync"/> instead
    /// batches the same join for its whole page in one query.
    /// </summary>
    private async Task<HandoverOperationalStatus> DeriveOperationalStatusAsync(
        Guid handoverId, HandoverStatus status, CancellationToken cancellationToken)
    {
        if (status != HandoverStatus.Finalized)
        {
            return HandoverOperationalStatusRules.Derive(status, hasSignedCurrentTerm: false);
        }

        var currentTermId = await dbContext.ResponsibilityTerms.AsNoTracking()
            .Where(t => t.EquipmentHandoverId == handoverId && t.Status == ResponsibilityTermStatus.Generated)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var hasSignedCurrentTerm = currentTermId is not null &&
            await dbContext.ResponsibilityTermSignedArtifacts.AsNoTracking()
                .AnyAsync(a => a.ResponsibilityTermId == currentTermId, cancellationToken);

        return HandoverOperationalStatusRules.Derive(status, hasSignedCurrentTerm);
    }

    private static string BuildStorageKey(ImageContentType type)
    {
        var extension = type switch
        {
            ImageContentType.Jpeg => "jpg",
            ImageContentType.Png => "png",
            ImageContentType.WebP => "webp",
            _ => "bin",
        };

        var now = DateTime.UtcNow;
        return $"delivery-photos/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}.{extension}";
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Names every Laptop/MobilePhone item lacking a Current photo set by its real AssetTag, so HR can fix them all in one pass.</summary>
    private static string BuildNotDeliverableMessage(string assetTag) =>
        $"O equipamento {assetTag} já não está disponível para esta entrega. Remova-o desta entrega para continuar.";

    private static string BuildMissingPhotosMessage(List<string> assetTags) => assetTags.Count == 1
        ? $"Não é possível confirmar esta entrega.\nO equipamento {assetTags[0]} não tem fotografias atuais registadas. " +
          "Registe as 3 fotografias do equipamento antes de confirmar a entrega."
        : $"Não é possível confirmar esta entrega.\nOs equipamentos {string.Join(", ", assetTags)} não têm fotografias atuais registadas. " +
          "Registe as 3 fotografias de cada equipamento antes de confirmar a entrega.";
}
