using System.Data;
using System.Security.Cryptography;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Damages;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Files;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.Returns;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Files;
using EquipmentManagement.Infrastructure.Handovers;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EquipmentManagement.Infrastructure.Returns;

/// <summary>
/// The return/return-inspection/damage workflow (docs/PROJECT_SPEC.md, section
/// 12; Phase 5 scope). <see cref="ReceiveAsync"/> is the single point where a
/// consultant's custody actually ends (the assignment is set <c>Ended</c> there,
/// not at initiation or finalization — see ADR-039);
/// <see cref="FinalizeInspectionAsync"/> is the single point that can create a
/// <see cref="Damage"/> row or move <see cref="Equipment.Status"/> to
/// <see cref="EquipmentStatus.Available"/>/<see cref="EquipmentStatus.Damaged"/>.
/// <see cref="CancelAsync"/> is the only way out of a <see cref="ReturnStatus.Pending"/>
/// return besides <see cref="ReceiveAsync"/> — it reverses the pending-return
/// workflow only, never custody (the assignment stays <c>Active</c>; see ADR-044).
/// </summary>
public sealed class EquipmentReturnService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    AuditService auditService,
    IPrivateFileStorage fileStorage,
    IOptions<PrivateStorageOptions> storageOptions,
    EquipmentReturnInitiator returnInitiator,
    TimeProvider timeProvider)
{
    /// <summary>A sanity bound on one manual return request, far above any real kit.</summary>
    private const int MaxItemsPerReturnGroup = 50;

    public async Task<PagedResult<EquipmentReturnSummaryRecord>> ListAsync(
        EquipmentReturnListFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query =
            from r in dbContext.EquipmentReturns.AsNoTracking()
            join e in dbContext.Equipment.AsNoTracking() on r.EquipmentId equals e.Id
            join c in dbContext.Consultants.AsNoTracking() on r.ConsultantId equals c.Id
            select new { Return = r, AssetTag = e.AssetTag, ConsultantFullName = c.FullName };

        if (filter.ConsultantId is { } consultantId)
        {
            query = query.Where(x => x.Return.ConsultantId == consultantId);
        }

        if (filter.EquipmentId is { } equipmentId)
        {
            query = query.Where(x => x.Return.EquipmentId == equipmentId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Return.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.ConsultantFullName, $"%{filter.Search}%") ||
                EF.Functions.ILike(x.AssetTag, $"%{filter.Search}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(x => x.Return.CreatedAtUtc)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .Select(x => new EquipmentReturnSummaryRecord(
                x.Return.Id, x.Return.EquipmentAssignmentId, x.Return.EquipmentId, x.AssetTag, x.Return.ConsultantId,
                x.ConsultantFullName, x.Return.Status, x.Return.Reason, x.Return.ExpectedReturnDate, x.Return.ReceivedOn,
                x.Return.CreatedAtUtc, x.Return.CompletedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<EquipmentReturnSummaryRecord>(items, page1Based, boundedPageSize, totalCount);
    }

    public async Task<EquipmentReturnDetailRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await (
                from r in dbContext.EquipmentReturns.AsNoTracking()
                join e in dbContext.Equipment.AsNoTracking() on r.EquipmentId equals e.Id
                join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
                join c in dbContext.Consultants.AsNoTracking() on r.ConsultantId equals c.Id
                where r.Id == id
                select new { Return = r, AssetTag = e.AssetTag, TypeName = t.Name, ConsultantFullName = c.FullName })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        var inspectionRecord = await BuildInspectionRecordAsync(id, cancellationToken);
        return await ToDetailRecordAsync(result.Return, result.AssetTag, result.TypeName, result.ConsultantFullName, inspectionRecord, cancellationToken);
    }

    /// <summary>
    /// Starts the return of one equipment from its own page. Only an
    /// <c>Assigned</c> equipment backed by an <c>Active</c> assignment can have
    /// a return initiated (Phase 5 scope). It is the one-item case of <see cref="InitiateGroupAsync"/>
    /// and runs through the same operation, so both entry points share their
    /// rules. Consultant identity is always derived from the assignment, never
    /// accepted as caller input, so it can never mismatch.
    /// </summary>
    public async Task<ServiceResult<EquipmentReturnDetailRecord>> InitiateAsync(
        Guid equipmentAssignmentId, InitiateReturnData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var started = await StartAsync(consultantId: null, [equipmentAssignmentId], data, actorUserId, actorDisplayName, cancellationToken);
        if (!started.Succeeded)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(started.ErrorCode, started.ErrorMessage!);
        }

        var detail = await GetByIdAsync(started.Value!.ReturnIds[0], cancellationToken);
        return ServiceResult.Ok(detail!);
    }

    /// <summary>
    /// Starts the return of one or more pieces of equipment held by one
    /// Consultant, from the Returns area (Iteration 4). Each item
    /// gets its own independent <see cref="EquipmentReturn"/>; they share one
    /// <see cref="EquipmentReturnGroup"/> only so the Returns area can show
    /// them as one collection. All or nothing: if any selected item cannot
    /// have a return initiated, none is. Eligibility is current custody (an
    /// <c>Active</c> assignment) and nothing else — the Consultant's
    /// allocation is neither required nor touched, and no email is sent, the
    /// same as a return started from an equipment's page.
    /// </summary>
    public Task<ServiceResult<StartedReturnGroupRecord>> InitiateGroupAsync(
        Guid consultantId, IReadOnlyList<Guid> equipmentAssignmentIds, InitiateReturnData data, Guid actorUserId, string actorDisplayName,
        CancellationToken cancellationToken = default) =>
        StartAsync(consultantId, equipmentAssignmentIds, data, actorUserId, actorDisplayName, cancellationToken);

    private async Task<ServiceResult<StartedReturnGroupRecord>> StartAsync(
        Guid? consultantId, IReadOnlyList<Guid> equipmentAssignmentIds, InitiateReturnData data, Guid actorUserId, string actorDisplayName,
        CancellationToken cancellationToken)
    {
        var assignmentIds = equipmentAssignmentIds.Distinct().ToList();
        if (assignmentIds.Count == 0)
        {
            return ServiceResult.Fail<StartedReturnGroupRecord>(ServiceErrorCode.Validation, "Selecione pelo menos um equipamento para devolver.");
        }

        if (assignmentIds.Count > MaxItemsPerReturnGroup)
        {
            return ServiceResult.Fail<StartedReturnGroupRecord>(
                ServiceErrorCode.Validation, $"Não é possível iniciar mais de {MaxItemsPerReturnGroup} devoluções de uma só vez.");
        }

        try
        {
            return await StartInTransactionAsync(consultantId, assignmentIds, data, actorUserId, actorDisplayName, cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolationOn(ex, EquipmentReturnInitiator.OneOpenReturnPerAssignmentIndexName))
        {
            // A concurrent request already inserted a non-Cancelled return for
            // one of these assignments after this transaction read it as
            // having none — the partial unique index (Status <> 'Cancelled')
            // is the actual backstop; see PostgresConflictClassifier for why
            // this walks the full exception chain. Nothing was persisted.
            return ServiceResult.Fail<StartedReturnGroupRecord>(
                ServiceErrorCode.ReturnAlreadyInProgress, "Já existe uma devolução em curso para um dos equipamentos selecionados. Nenhuma devolução foi iniciada.");
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsSerializationFailure(ex))
        {
            return ServiceResult.Fail<StartedReturnGroupRecord>(
                ServiceErrorCode.Concurrency, "Um dos equipamentos selecionados foi alterado por outra operação. Atualize a página e tente novamente.");
        }
    }

    /// <summary>
    /// Every row this method decides on or mutates is loaded AFTER opening the
    /// Serializable transaction (see ReceiveAsync's remarks and ADR-042). The group, all the returns, the equipment
    /// transitions and the audit entries commit together or not at all;
    /// returning a failure disposes the transaction uncommitted.
    /// </summary>
    private async Task<ServiceResult<StartedReturnGroupRecord>> StartInTransactionAsync(
        Guid? consultantId, List<Guid> assignmentIds, InitiateReturnData data, Guid actorUserId, string actorDisplayName,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // Equipment is tracked: initiating a return changes its status.
        var items = await (
            from a in dbContext.EquipmentAssignments
            join e in dbContext.Equipment on a.EquipmentId equals e.Id
            where assignmentIds.Contains(a.Id)
            orderby e.AssetTag
            select new { Assignment = a, Equipment = e }).ToListAsync(cancellationToken);

        if (items.Count != assignmentIds.Count)
        {
            return ServiceResult.Fail<StartedReturnGroupRecord>(ServiceErrorCode.NotFound, "Equipment assignment not found.");
        }

        // One group is one Consultant's collection. The Consultant is the one
        // the assignments say, never caller input alone.
        var holderId = consultantId ?? items[0].Assignment.ConsultantId;
        if (items.Any(x => x.Assignment.ConsultantId != holderId))
        {
            return ServiceResult.Fail<StartedReturnGroupRecord>(
                ServiceErrorCode.Validation, "Os equipamentos selecionados não estão todos atribuídos a este consultor.");
        }

        var assignmentsWithOpenReturn = (await dbContext.EquipmentReturns.AsNoTracking()
                .Where(r => assignmentIds.Contains(r.EquipmentAssignmentId) && r.Status != ReturnStatus.Cancelled)
                .Select(r => r.EquipmentAssignmentId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var group = returnInitiator.StageGroup(holderId, consultantAllocationId: null, actorUserId, actorDisplayName);
        var created = new List<(EquipmentReturn Return, Equipment Equipment)>();
        foreach (var item in items)
        {
            var hasOpenReturn = assignmentsWithOpenReturn.Contains(item.Assignment.Id);
            var staged = returnInitiator.Stage(group, item.Assignment, item.Equipment, hasOpenReturn, data, actorUserId, actorDisplayName);
            if (!staged.Succeeded)
            {
                return ServiceResult.Fail<StartedReturnGroupRecord>(
                    staged.ErrorCode, BuildCannotInitiateMessage(item.Equipment.AssetTag, item.Assignment.Status, hasOpenReturn, items.Count));
            }

            created.Add((staged.Value!, item.Equipment));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await returnInitiator.RecordCreatedAsync(
            group, created, EquipmentReturnInitiator.ManualTrigger, actorUserId, actorDisplayName, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ServiceResult.Ok(new StartedReturnGroupRecord(group.Id, created.Select(x => x.Return.Id).ToList()));
    }

    private static string BuildCannotInitiateMessage(string assetTag, AssignmentStatus assignmentStatus, bool hasOpenReturn, int requestedCount)
    {
        var reason = assignmentStatus != AssignmentStatus.Active
            ? $"O equipamento {assetTag} já não está atribuído ao consultor."
            : hasOpenReturn
                ? $"O equipamento {assetTag} já tem uma devolução em curso."
                : $"O equipamento {assetTag} não está num estado que permita iniciar uma devolução.";

        return requestedCount > 1 ? $"{reason} Nenhuma devolução foi iniciada." : reason;
    }

    /// <summary>
    /// The custody-ending operation (see ADR-039): physical
    /// receipt is what actually ends the consultant's responsibility, not
    /// initiation and not inspection finalization. The receiver may be a
    /// different internal user than the one recording this action
    /// (docs/PROJECT_SPEC.md, section 12) — snapshotted here by name, never a
    /// live lookup (Phase 5 scope).
    /// </summary>
    public async Task<ServiceResult<EquipmentReturnDetailRecord>> ReceiveAsync(
        Guid returnId, ReceiveReturnData data, IReadOnlyList<ReceivedPhotoFile> photos, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var receiver = await userManager.FindByIdAsync(data.ReceivedByUserId.ToString());
        if (receiver is null)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.NotFound, "Receiving user not found.");
        }

        if (!receiver.IsActive)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.InactiveReference, "The receiving user is not active.");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (!ReturnRules.IsValidReceivedOn(data.ReceivedOn, today))
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.Validation, "Receipt date cannot be in the future.");
        }

        // Kind never changes once an EquipmentType is created (see ADR-027),
        // so deciding the photo requirement from an advance, untracked read is
        // safe even though the actual state transition below re-validates
        // everything else inside the Serializable transaction.
        var preview = await (
            from r in dbContext.EquipmentReturns.AsNoTracking()
            join e in dbContext.Equipment.AsNoTracking() on r.EquipmentId equals e.Id
            join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
            where r.Id == returnId
            select new { t.Kind }).FirstOrDefaultAsync(cancellationToken);
        if (preview is null)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.NotFound, "Return not found.");
        }

        var photosRequired = EquipmentRules.RequiresConditionPhotos(preview.Kind);
        if (photosRequired && photos.Count != ReturnRules.RequiredReceivePhotoCount)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(
                ServiceErrorCode.Validation,
                $"É necessário enviar exatamente {ReturnRules.RequiredReceivePhotoCount} fotografias do estado do equipamento para concluir a receção.");
        }

        if (!photosRequired && photos.Count > 0)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(
                ServiceErrorCode.Validation, "Este tipo de equipamento não requer fotografias na receção.");
        }

        // Bytes are read and validated (real image signature, size, hash) in
        // memory before the transaction even opens — pure, fast, no storage
        // I/O yet, so a validation failure here needs no cleanup. The actual
        // storage writes happen later, only once every lifecycle check inside
        // the transaction has passed (see below).
        var preparedPhotos = new List<(byte[] Bytes, ImageContentType DetectedType, string OriginalFileName, string DeclaredContentType, string Sha256)>();
        foreach (var photo in photos)
        {
            var (error, bytes) = await ValidateAndReadImageAsync(photo.Content, photo.DeclaredContentType, cancellationToken);
            if (error is { } validationError)
            {
                return ServiceResult.Fail<EquipmentReturnDetailRecord>(validationError.Code, validationError.Message);
            }

            var detected = ImageSignatureDetector.Detect(bytes)!.Value;
            preparedPhotos.Add((bytes, detected, Trim(photo.OriginalFileName) ?? "photo", photo.DeclaredContentType, Convert.ToHexStringLower(SHA256.HashData(bytes))));
        }

        // Every row this method mutates is loaded AFTER opening the
        // Serializable transaction, not before — the read that decides
        // "is this return still Pending" must itself be inside the
        // serializable snapshot for Postgres to detect a genuine concurrent
        // double-receipt (there is no fresh-unique-insert backstop here, only
        // a same-row status transition — see PostgresConflictClassifier's
        // remarks and ADR-042).
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var equipmentReturn = await dbContext.EquipmentReturns.FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);
        if (equipmentReturn is null)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.NotFound, "Return not found.");
        }

        if (!ReturnRules.CanReceive(equipmentReturn.Status))
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.ReturnNotPending, "This return is not pending physical receipt.");
        }

        var assignment = await dbContext.EquipmentAssignments.FirstAsync(a => a.Id == equipmentReturn.EquipmentAssignmentId, cancellationToken);
        if (assignment.Status != AssignmentStatus.Active)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.InvalidLifecycleTransition, "This assignment has already ended.");
        }

        var equipment = await dbContext.Equipment.FirstAsync(e => e.Id == equipmentReturn.EquipmentId, cancellationToken);
        if (equipment.Status != EquipmentStatus.ReturnPending)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(
                ServiceErrorCode.InvalidLifecycleTransition, $"Equipment in status '{equipment.Status}' cannot be received.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        equipmentReturn.Status = ReturnStatus.Received;
        equipmentReturn.ReceivedOn = data.ReceivedOn;
        equipmentReturn.ReceivedAtUtc = now;
        equipmentReturn.ReceivedByUserId = receiver.Id;
        equipmentReturn.ReceivedByDisplayNameSnapshot = $"{receiver.FirstName} {receiver.LastName}";
        equipmentReturn.UpdatedAtUtc = now;

        assignment.Status = AssignmentStatus.Ended;
        assignment.EndedAtUtc = now;
        assignment.UpdatedAtUtc = now;

        equipment.Status = EquipmentStatus.UnderInspection;
        equipment.UpdatedAtUtc = now;

        // Storage writes happen last, only once every lifecycle check above
        // has passed — this is the only place in this method that touches
        // private file storage, so a failure anywhere above never leaves an
        // orphaned object behind. Current photos are superseded (never
        // deleted) atomically with the same SaveChangesAsync/commit as the
        // status transitions above; if that commit fails, the newly-saved
        // storage keys are removed in the catch block and current photos
        // never change.
        EquipmentPhotoSet? newPhotoSet = null;
        var newlySavedStorageKeys = new List<string>();
        if (photosRequired)
        {
            var previousCurrent = await dbContext.EquipmentPhotoSets
                .FirstOrDefaultAsync(s => s.EquipmentId == equipment.Id && s.Status == PhotoSetStatus.Current, cancellationToken);
            if (previousCurrent is not null)
            {
                previousCurrent.Status = PhotoSetStatus.Historical;
            }

            newPhotoSet = new EquipmentPhotoSet
            {
                Id = Guid.NewGuid(),
                EquipmentId = equipment.Id,
                Status = PhotoSetStatus.Current,
                Source = PhotoSetSource.Return,
                EquipmentReturnId = returnId,
                CreatedAtUtc = now,
                CreatedByUserId = actorUserId,
                CreatedByDisplayNameSnapshot = actorDisplayName,
            };
            dbContext.EquipmentPhotoSets.Add(newPhotoSet);

            var sortOrder = 1;
            foreach (var photo in preparedPhotos)
            {
                var storageKey = BuildStorageKey(returnId, "condition", photo.DetectedType);
                await SaveBytesAsync(storageKey, photo.Bytes, cancellationToken);
                newlySavedStorageKeys.Add(storageKey);

                dbContext.EquipmentPhotoSetPhotos.Add(new EquipmentPhotoSetPhoto
                {
                    Id = Guid.NewGuid(),
                    EquipmentPhotoSetId = newPhotoSet.Id,
                    StorageKey = storageKey,
                    OriginalFileName = photo.OriginalFileName,
                    ContentType = photo.DeclaredContentType,
                    SizeBytes = photo.Bytes.LongLength,
                    Sha256Hash = photo.Sha256,
                    SortOrder = sortOrder++,
                    UploadedAtUtc = now,
                    UploadedByUserId = actorUserId,
                });
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentReturn.Received,
                EntityType: "EquipmentReturn",
                EntityId: equipmentReturn.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { equipment.AssetTag, ReceivedOn = data.ReceivedOn, ReceivedBy = equipmentReturn.ReceivedByDisplayNameSnapshot }),
                cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentAssignment.Ended,
                EntityType: "Equipment",
                EntityId: equipment.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { equipment.AssetTag, ConsultantId = assignment.ConsultantId }), cancellationToken);

            if (newPhotoSet is not null)
            {
                await auditService.RecordAsync(new AuditEntry(
                    AuditActions.EquipmentPhotoSet.Updated,
                    EntityType: "Equipment",
                    EntityId: equipment.Id.ToString(),
                    ActorUserId: actorUserId,
                    ActorDisplayNameSnapshot: actorDisplayName,
                    NewValues: new { equipment.AssetTag, PhotoSetId = newPhotoSet.Id, EquipmentReturnId = returnId }), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsSerializationFailure(ex))
        {
            foreach (var key in newlySavedStorageKeys)
            {
                await fileStorage.DeleteAsync(key, cancellationToken);
            }

            return ServiceResult.Fail<EquipmentReturnDetailRecord>(
                ServiceErrorCode.Concurrency, "This return was already processed by another operation; please retry.");
        }

        var consultant = await dbContext.Consultants.AsNoTracking().FirstAsync(c => c.Id == equipmentReturn.ConsultantId, cancellationToken);
        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);
        return ServiceResult.Ok(await ToDetailRecordAsync(equipmentReturn, equipment.AssetTag, type.Name, consultant.FullName, null, cancellationToken));
    }

    /// <summary>
    /// Calls off a return before physical receipt (Phase 5 cancellation
    /// amendment) — HR/Administration realized the return was started by
    /// mistake, offboarding was cancelled, or the consultant will keep the
    /// equipment after all. Reverses only the pending-return workflow, never
    /// custody: the assignment stays <c>Active</c> and is never touched here,
    /// no new assignment is created, and the original Handover is untouched.
    /// The cancelled <see cref="EquipmentReturn"/> row itself is never deleted
    /// — it remains historical, queryable evidence that a return attempt was
    /// made and called off.
    /// </summary>
    public async Task<ServiceResult<EquipmentReturnDetailRecord>> CancelAsync(
        Guid returnId, CancelReturnData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        // Same rationale as ReceiveAsync: this is a plain same-row status
        // transition with no fresh-insert backstop, and it races ReceiveAsync
        // directly (Cancel vs Receive) — every mutated row must be loaded
        // AFTER opening the Serializable transaction for Postgres to detect a
        // genuine concurrent conflict between the two.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var equipmentReturn = await dbContext.EquipmentReturns.FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);
        if (equipmentReturn is null)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.NotFound, "Return not found.");
        }

        if (!ReturnRules.CanCancel(equipmentReturn.Status))
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(
                ServiceErrorCode.ReturnNotPending, "This return can no longer be cancelled — it is not pending physical receipt.");
        }

        var assignment = await dbContext.EquipmentAssignments.FirstAsync(a => a.Id == equipmentReturn.EquipmentAssignmentId, cancellationToken);
        if (assignment.Status != AssignmentStatus.Active)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.InvalidLifecycleTransition, "This assignment has already ended.");
        }

        var equipment = await dbContext.Equipment.FirstAsync(e => e.Id == equipmentReturn.EquipmentId, cancellationToken);
        if (equipment.Status != EquipmentStatus.ReturnPending)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(
                ServiceErrorCode.InvalidLifecycleTransition, $"Equipment in status '{equipment.Status}' cannot have its return cancelled.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        equipmentReturn.Status = ReturnStatus.Cancelled;
        equipmentReturn.CancelledAtUtc = now;
        equipmentReturn.CancelledByUserId = actorUserId;
        equipmentReturn.CancelledByDisplayNameSnapshot = actorDisplayName;
        equipmentReturn.CancellationReason = Trim(data.Reason);
        equipmentReturn.UpdatedAtUtc = now;

        // Assignment is deliberately left untouched — it was never ended.
        equipment.Status = EquipmentStatus.Assigned;
        equipment.UpdatedAtUtc = now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            // Free-text CancellationReason is deliberately excluded — see
            // AuditActions.EquipmentReturn.Cancelled's remarks.
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentReturn.Cancelled,
                EntityType: "EquipmentReturn",
                EntityId: equipmentReturn.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { equipment.AssetTag, EquipmentAssignmentId = assignment.Id }), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsSerializationFailure(ex))
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(
                ServiceErrorCode.Concurrency, "This return was already processed by another operation; please retry.");
        }

        var consultant = await dbContext.Consultants.AsNoTracking().FirstAsync(c => c.Id == equipmentReturn.ConsultantId, cancellationToken);
        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);
        return ServiceResult.Ok(await ToDetailRecordAsync(equipmentReturn, equipment.AssetTag, type.Name, consultant.FullName, null, cancellationToken));
    }

    /// <summary>Create-or-update: the first call creates the inspection, later calls update it — same upsert shape as <c>EquipmentHandoverService.RecordInspectionAsync</c>.</summary>
    public async Task<ServiceResult<ReturnInspectionRecord>> RecordInspectionAsync(
        Guid returnId, UpdateReturnInspectionData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var equipmentReturn = await dbContext.EquipmentReturns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);
        if (equipmentReturn is null)
        {
            return ServiceResult.Fail<ReturnInspectionRecord>(ServiceErrorCode.NotFound, "Return not found.");
        }

        if (!ReturnRules.CanRecordInspection(equipmentReturn.Status))
        {
            return ServiceResult.Fail<ReturnInspectionRecord>(
                ServiceErrorCode.ReturnNotReceived, "The equipment has not yet been physically received, or this return has already been finalized.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var inspection = await dbContext.ReturnInspections.FirstOrDefaultAsync(i => i.EquipmentReturnId == returnId, cancellationToken);
        if (inspection is null)
        {
            inspection = new ReturnInspection
            {
                Id = Guid.NewGuid(),
                EquipmentReturnId = returnId,
                Condition = data.Condition,
                Notes = Trim(data.Notes),
                HasDamage = data.HasDamage,
                DamageDescription = Trim(data.DamageDescription),
                InspectedByUserId = actorUserId,
                InspectedByDisplayNameSnapshot = actorDisplayName,
                InspectedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            dbContext.ReturnInspections.Add(inspection);
        }
        else
        {
            inspection.Condition = data.Condition;
            inspection.Notes = Trim(data.Notes);
            inspection.HasDamage = data.HasDamage;
            inspection.DamageDescription = Trim(data.DamageDescription);
            inspection.InspectedByUserId = actorUserId;
            inspection.InspectedByDisplayNameSnapshot = actorDisplayName;
            inspection.InspectedAtUtc = now;
            inspection.UpdatedAtUtc = now;
        }

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.ReturnInspection.Recorded,
            EntityType: "EquipmentReturn",
            EntityId: returnId.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { Condition = inspection.Condition.ToString(), inspection.HasDamage }), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        var photos = await BuildInspectionPhotoRecordsAsync(inspection.Id, cancellationToken);
        var damagePhotos = await BuildDamagePhotoRecordsAsync(inspection.Id, cancellationToken);
        return ServiceResult.Ok(ToInspectionRecord(inspection, photos, damagePhotos, null));
    }

    public async Task<ServiceResult<ReturnInspectionPhotoRecord>> UploadInspectionPhotoAsync(
        Guid returnId, Stream content, string originalFileName, string declaredContentType,
        Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var equipmentReturn = await dbContext.EquipmentReturns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);
        if (equipmentReturn is null)
        {
            return ServiceResult.Fail<ReturnInspectionPhotoRecord>(ServiceErrorCode.NotFound, "Return not found.");
        }

        if (!ReturnRules.CanRecordInspection(equipmentReturn.Status))
        {
            return ServiceResult.Fail<ReturnInspectionPhotoRecord>(
                ServiceErrorCode.ReturnNotReceived, "The equipment has not yet been physically received, or this return has already been finalized.");
        }

        var inspection = await dbContext.ReturnInspections.AsNoTracking().FirstOrDefaultAsync(i => i.EquipmentReturnId == returnId, cancellationToken);
        if (inspection is null)
        {
            return ServiceResult.Fail<ReturnInspectionPhotoRecord>(ServiceErrorCode.InspectionMissing, "Record the return inspection before uploading photos.");
        }

        var (error, bytes) = await ValidateAndReadImageAsync(content, declaredContentType, cancellationToken);
        if (error is not null)
        {
            return ServiceResult.Fail<ReturnInspectionPhotoRecord>(error.Value.Code, error.Value.Message);
        }

        var detected = ImageSignatureDetector.Detect(bytes)!.Value;
        var storageKey = BuildStorageKey(returnId, "inspection", detected);
        await SaveBytesAsync(storageKey, bytes, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var sortOrder = await dbContext.ReturnInspectionPhotos.CountAsync(p => p.ReturnInspectionId == inspection.Id, cancellationToken);

        var photo = new ReturnInspectionPhoto
        {
            Id = Guid.NewGuid(),
            ReturnInspectionId = inspection.Id,
            StorageKey = storageKey,
            OriginalFileName = Trim(originalFileName) ?? "photo",
            ContentType = declaredContentType,
            SizeBytes = bytes.LongLength,
            Sha256Hash = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            SortOrder = sortOrder,
            UploadedAtUtc = now,
            UploadedByUserId = actorUserId,
        };

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
            dbContext.ReturnInspectionPhotos.Add(photo);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ReturnInspection.PhotoAdded,
                EntityType: "EquipmentReturn",
                EntityId: returnId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { photo.OriginalFileName, photo.SizeBytes }), cancellationToken);
            await transaction.CommitIfOwnedAsync(cancellationToken);
        }
        catch
        {
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
            throw;
        }

        return ServiceResult.Ok(new ReturnInspectionPhotoRecord(photo.Id, photo.OriginalFileName, photo.ContentType, photo.SizeBytes, photo.SortOrder, photo.UploadedAtUtc));
    }

    public async Task<ServiceResult> DeleteInspectionPhotoAsync(
        Guid returnId, Guid photoId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var equipmentReturn = await dbContext.EquipmentReturns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);
        if (equipmentReturn is null)
        {
            return ServiceResult.Fail(ServiceErrorCode.NotFound, "Return not found.");
        }

        if (!ReturnRules.CanRecordInspection(equipmentReturn.Status))
        {
            return ServiceResult.Fail(
                ServiceErrorCode.ReturnNotReceived, "The equipment has not yet been physically received, or this return has already been finalized.");
        }

        var photo = await (
            from p in dbContext.ReturnInspectionPhotos
            join i in dbContext.ReturnInspections on p.ReturnInspectionId equals i.Id
            where p.Id == photoId && i.EquipmentReturnId == returnId
            select p).FirstOrDefaultAsync(cancellationToken);

        if (photo is null)
        {
            return ServiceResult.Fail(ServiceErrorCode.NotFound, "Photo not found.");
        }

        var storageKey = photo.StorageKey;
        dbContext.ReturnInspectionPhotos.Remove(photo);

        await using (var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken))
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ReturnInspection.PhotoRemoved,
                EntityType: "EquipmentReturn",
                EntityId: returnId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { PhotoId = photoId }), cancellationToken);
            await transaction.CommitIfOwnedAsync(cancellationToken);
        }

        await fileStorage.DeleteAsync(storageKey, cancellationToken);

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Validates the full <paramref name="returnId"/>/<paramref name="photoId"/>
    /// chain, not just that <paramref name="photoId"/> exists somewhere — same
    /// defense-in-depth rationale as <c>EquipmentHandoverService.GetPhotoContentAsync</c>
    /// (Phase 10 hardening).
    /// </summary>
    public async Task<(Stream Content, string ContentType, string OriginalFileName)?> GetInspectionPhotoContentAsync(
        Guid returnId, Guid photoId, CancellationToken cancellationToken = default)
    {
        var photo = await (
                from p in dbContext.ReturnInspectionPhotos.AsNoTracking()
                join inspection in dbContext.ReturnInspections.AsNoTracking() on p.ReturnInspectionId equals inspection.Id
                where p.Id == photoId && inspection.EquipmentReturnId == returnId
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
    /// Damage evidence is attached to the inspection (see <see cref="DamagePhoto"/>'s
    /// remarks), not to a <see cref="Damage"/> row — that row is only
    /// materialized at finalization.
    /// </summary>
    public async Task<ServiceResult<DamagePhotoRecord>> UploadDamagePhotoAsync(
        Guid returnId, Stream content, string originalFileName, string declaredContentType,
        Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var equipmentReturn = await dbContext.EquipmentReturns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);
        if (equipmentReturn is null)
        {
            return ServiceResult.Fail<DamagePhotoRecord>(ServiceErrorCode.NotFound, "Return not found.");
        }

        if (!ReturnRules.CanRecordInspection(equipmentReturn.Status))
        {
            return ServiceResult.Fail<DamagePhotoRecord>(
                ServiceErrorCode.ReturnNotReceived, "The equipment has not yet been physically received, or this return has already been finalized.");
        }

        var inspection = await dbContext.ReturnInspections.AsNoTracking().FirstOrDefaultAsync(i => i.EquipmentReturnId == returnId, cancellationToken);
        if (inspection is null)
        {
            return ServiceResult.Fail<DamagePhotoRecord>(ServiceErrorCode.InspectionMissing, "Record the return inspection before uploading damage photos.");
        }

        var (error, bytes) = await ValidateAndReadImageAsync(content, declaredContentType, cancellationToken);
        if (error is not null)
        {
            return ServiceResult.Fail<DamagePhotoRecord>(error.Value.Code, error.Value.Message);
        }

        var detected = ImageSignatureDetector.Detect(bytes)!.Value;
        var storageKey = BuildStorageKey(returnId, "damage", detected);
        await SaveBytesAsync(storageKey, bytes, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var sortOrder = await dbContext.DamagePhotos.CountAsync(p => p.ReturnInspectionId == inspection.Id, cancellationToken);

        var photo = new DamagePhoto
        {
            Id = Guid.NewGuid(),
            ReturnInspectionId = inspection.Id,
            StorageKey = storageKey,
            OriginalFileName = Trim(originalFileName) ?? "photo",
            ContentType = declaredContentType,
            SizeBytes = bytes.LongLength,
            Sha256Hash = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            SortOrder = sortOrder,
            UploadedAtUtc = now,
            UploadedByUserId = actorUserId,
        };

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
            dbContext.DamagePhotos.Add(photo);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.Damage.PhotoAdded,
                EntityType: "EquipmentReturn",
                EntityId: returnId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { photo.OriginalFileName, photo.SizeBytes }), cancellationToken);
            await transaction.CommitIfOwnedAsync(cancellationToken);
        }
        catch
        {
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
            throw;
        }

        return ServiceResult.Ok(new DamagePhotoRecord(photo.Id, photo.OriginalFileName, photo.ContentType, photo.SizeBytes, photo.SortOrder, photo.UploadedAtUtc));
    }

    public async Task<ServiceResult> DeleteDamagePhotoAsync(
        Guid returnId, Guid photoId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var equipmentReturn = await dbContext.EquipmentReturns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);
        if (equipmentReturn is null)
        {
            return ServiceResult.Fail(ServiceErrorCode.NotFound, "Return not found.");
        }

        if (!ReturnRules.CanRecordInspection(equipmentReturn.Status))
        {
            return ServiceResult.Fail(
                ServiceErrorCode.ReturnNotReceived, "The equipment has not yet been physically received, or this return has already been finalized.");
        }

        var photo = await (
            from p in dbContext.DamagePhotos
            join i in dbContext.ReturnInspections on p.ReturnInspectionId equals i.Id
            where p.Id == photoId && i.EquipmentReturnId == returnId
            select p).FirstOrDefaultAsync(cancellationToken);

        if (photo is null)
        {
            return ServiceResult.Fail(ServiceErrorCode.NotFound, "Photo not found.");
        }

        var storageKey = photo.StorageKey;
        dbContext.DamagePhotos.Remove(photo);

        await using (var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken))
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.Damage.PhotoRemoved,
                EntityType: "EquipmentReturn",
                EntityId: returnId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { PhotoId = photoId }), cancellationToken);
            await transaction.CommitIfOwnedAsync(cancellationToken);
        }

        await fileStorage.DeleteAsync(storageKey, cancellationToken);

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Validates the full <paramref name="returnId"/>/<paramref name="photoId"/>
    /// chain — same defense-in-depth rationale as
    /// <see cref="GetInspectionPhotoContentAsync"/> (Phase 10 hardening).
    /// </summary>
    public async Task<(Stream Content, string ContentType, string OriginalFileName)?> GetDamagePhotoContentAsync(
        Guid returnId, Guid photoId, CancellationToken cancellationToken = default)
    {
        var photo = await (
                from p in dbContext.DamagePhotos.AsNoTracking()
                join inspection in dbContext.ReturnInspections.AsNoTracking() on p.ReturnInspectionId equals inspection.Id
                where p.Id == photoId && inspection.EquipmentReturnId == returnId
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
    /// The only method that can create a <see cref="Damage"/> row or move
    /// equipment to <see cref="EquipmentStatus.Available"/>/<see cref="EquipmentStatus.Damaged"/>
    /// (docs/PROJECT_SPEC.md, invariant #11). Never sends equipment to
    /// <see cref="EquipmentStatus.UnderMaintenance"/> directly — that is a
    /// separate, explicit decision (<c>MaintenanceService.StartAsync</c>).
    /// </summary>
    public async Task<ServiceResult<EquipmentReturnDetailRecord>> FinalizeInspectionAsync(
        Guid returnId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        // Every row this method mutates is loaded AFTER opening the
        // Serializable transaction — see ReceiveAsync's remarks for why. Unlike
        // EquipmentHandoverService.FinalizeAsync, a no-damage finalize has no
        // fresh-unique-insert backstop, so the read that decides "is this
        // still finalizable" must itself be inside the serializable snapshot.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var equipmentReturn = await dbContext.EquipmentReturns.FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);
        if (equipmentReturn is null)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.NotFound, "Return not found.");
        }

        if (!ReturnRules.CanFinalize(equipmentReturn.Status))
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.ReturnNotReceived, "This return is not ready to be finalized.");
        }

        var inspection = await dbContext.ReturnInspections.FirstOrDefaultAsync(i => i.EquipmentReturnId == returnId, cancellationToken);
        if (inspection is null)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.InspectionMissing, "Record the return inspection before finalizing.");
        }

        var equipment = await dbContext.Equipment.FirstOrDefaultAsync(e => e.Id == equipmentReturn.EquipmentId, cancellationToken);
        if (equipment is null)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(ServiceErrorCode.NotFound, "Equipment not found.");
        }

        if (equipment.Status != EquipmentStatus.UnderInspection)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(
                ServiceErrorCode.InvalidLifecycleTransition, $"Equipment in status '{equipment.Status}' cannot be finalized.");
        }

        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);

        // ReturnInspectionPhoto is optional additional evidence only — the
        // official condition-photo refresh for Laptop/MobilePhone equipment
        // already happened at physical receipt (see ReceiveAsync), so
        // inspection finalization no longer requires a second, duplicate
        // 3-photo set.
        if (inspection.HasDamage)
        {
            var damagePhotoCount = await dbContext.DamagePhotos.CountAsync(p => p.ReturnInspectionId == inspection.Id, cancellationToken);
            if (!ReturnRules.HasRequiredDamageEvidence(inspection.DamageDescription, damagePhotoCount))
            {
                return ServiceResult.Fail<EquipmentReturnDetailRecord>(
                    ServiceErrorCode.DamageEvidenceRequired, "Damage requires a description and at least one damage photo before finalizing.");
            }
        }

        var spec = type.Kind == EquipmentTypeKind.Laptop
            ? await dbContext.LaptopSpecifications.AsNoTracking().FirstOrDefaultAsync(s => s.EquipmentId == equipment.Id, cancellationToken)
            : null;

        var now = timeProvider.GetUtcNow().UtcDateTime;

        inspection.EquipmentSnapshot = new EquipmentSnapshot
        {
            AssetTag = equipment.AssetTag,
            EquipmentTypeName = type.Name,
            Manufacturer = equipment.Manufacturer,
            Model = equipment.Model,
            SerialNumber = equipment.SerialNumber,
            Processor = spec?.Processor,
            Memory = spec?.Memory,
            Storage = spec?.Storage,
        };
        inspection.FinalizedAtUtc = now;
        inspection.UpdatedAtUtc = now;

        equipmentReturn.Status = ReturnStatus.Completed;
        equipmentReturn.CompletedAtUtc = now;
        equipmentReturn.UpdatedAtUtc = now;

        Damage? damage = null;
        if (inspection.HasDamage)
        {
            damage = new Damage
            {
                Id = Guid.NewGuid(),
                EquipmentId = equipment.Id,
                EquipmentReturnId = equipmentReturn.Id,
                ReturnInspectionId = inspection.Id,
                Description = inspection.DamageDescription!.Trim(),
                Status = DamageStatus.Open,
                ReportedAtUtc = now,
                ReportedByUserId = actorUserId,
                ReportedByDisplayNameSnapshot = actorDisplayName,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            dbContext.Damages.Add(damage);
            equipment.Status = EquipmentStatus.Damaged;
        }
        else
        {
            equipment.Status = EquipmentStatus.Available;
        }

        equipment.UpdatedAtUtc = now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ReturnInspection.Finalized,
                EntityType: "EquipmentReturn",
                EntityId: equipmentReturn.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { equipment.AssetTag, ResultingStatus = equipment.Status.ToString() }), cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentReturn.Completed,
                EntityType: "EquipmentReturn",
                EntityId: equipmentReturn.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName), cancellationToken);

            if (damage is not null)
            {
                // Free-text description is deliberately excluded from audit
                // metadata (Phase 5 scope) — the description is
                // sensitive operational free text, not an identifier.
                await auditService.RecordAsync(new AuditEntry(
                    AuditActions.Damage.Recorded,
                    EntityType: "Equipment",
                    EntityId: equipment.Id.ToString(),
                    ActorUserId: actorUserId,
                    ActorDisplayNameSnapshot: actorDisplayName,
                    Metadata: new { DamageId = damage.Id, equipment.AssetTag, HasDamage = true }), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.Find(ex, PostgresErrorCodes.UniqueViolation, PostgresErrorCodes.SerializationFailure) is not null)
        {
            return ServiceResult.Fail<EquipmentReturnDetailRecord>(
                ServiceErrorCode.Concurrency, "This return was already finalized by another operation; please retry.");
        }

        var consultant = await dbContext.Consultants.AsNoTracking().FirstAsync(c => c.Id == equipmentReturn.ConsultantId, cancellationToken);
        var inspectionRecord = await BuildInspectionRecordAsync(returnId, cancellationToken);
        return ServiceResult.Ok(await ToDetailRecordAsync(equipmentReturn, equipment.AssetTag, type.Name, consultant.FullName, inspectionRecord, cancellationToken));
    }

    private async Task<ReturnInspectionRecord?> BuildInspectionRecordAsync(Guid returnId, CancellationToken cancellationToken)
    {
        var inspection = await dbContext.ReturnInspections.AsNoTracking().FirstOrDefaultAsync(i => i.EquipmentReturnId == returnId, cancellationToken);
        if (inspection is null)
        {
            return null;
        }

        var photos = await BuildInspectionPhotoRecordsAsync(inspection.Id, cancellationToken);
        var damagePhotos = await BuildDamagePhotoRecordsAsync(inspection.Id, cancellationToken);
        var damage = await dbContext.Damages.AsNoTracking().FirstOrDefaultAsync(d => d.ReturnInspectionId == inspection.Id, cancellationToken);
        var damageRecord = damage is null
            ? null
            : new DamageRecord(damage.Id, damage.Description, damage.Status, damage.ReportedAtUtc, damage.ResolvedAtUtc, damagePhotos);

        return ToInspectionRecord(inspection, photos, damagePhotos, damageRecord);
    }

    private async Task<IReadOnlyList<ReturnInspectionPhotoRecord>> BuildInspectionPhotoRecordsAsync(Guid inspectionId, CancellationToken cancellationToken) =>
        await dbContext.ReturnInspectionPhotos.AsNoTracking()
            .Where(p => p.ReturnInspectionId == inspectionId)
            .OrderBy(p => p.SortOrder)
            .Select(p => new ReturnInspectionPhotoRecord(p.Id, p.OriginalFileName, p.ContentType, p.SizeBytes, p.SortOrder, p.UploadedAtUtc))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<DamagePhotoRecord>> BuildDamagePhotoRecordsAsync(Guid inspectionId, CancellationToken cancellationToken) =>
        await dbContext.DamagePhotos.AsNoTracking()
            .Where(p => p.ReturnInspectionId == inspectionId)
            .OrderBy(p => p.SortOrder)
            .Select(p => new DamagePhotoRecord(p.Id, p.OriginalFileName, p.ContentType, p.SizeBytes, p.SortOrder, p.UploadedAtUtc))
            .ToListAsync(cancellationToken);

    /// <summary>Shared by both photo-upload methods — validates size then real image signature, never trusting the declared Content-Type alone.</summary>
    private async Task<((ServiceErrorCode Code, string Message)? Error, byte[] Bytes)> ValidateAndReadImageAsync(
        Stream content, string declaredContentType, CancellationToken cancellationToken)
    {
        var maxSize = storageOptions.Value.MaxPhotoSizeBytes;
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > maxSize)
        {
            return ((ServiceErrorCode.FileTooLarge, $"Photo must be between 1 byte and {maxSize} bytes."), []);
        }

        var bytes = buffer.ToArray();
        var detected = ImageSignatureDetector.Detect(bytes);
        if (detected is null || !ImageSignatureDetector.MatchesDeclaredContentType(detected.Value, declaredContentType))
        {
            return ((ServiceErrorCode.InvalidFile, "Only genuine JPEG, PNG or WebP images are accepted."), []);
        }

        return (null, bytes);
    }

    private async Task SaveBytesAsync(string storageKey, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream(bytes);
        await fileStorage.SaveAsync(storageKey, buffer, cancellationToken);
    }

    private static string BuildStorageKey(Guid returnId, string category, ImageContentType type)
    {
        var extension = type switch
        {
            ImageContentType.Jpeg => "jpg",
            ImageContentType.Png => "png",
            ImageContentType.WebP => "webp",
            _ => "bin",
        };

        return $"returns/{returnId:N}/{category}/{Guid.NewGuid():N}.{extension}";
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ReturnInspectionRecord ToInspectionRecord(
        ReturnInspection inspection, IReadOnlyList<ReturnInspectionPhotoRecord> photos, IReadOnlyList<DamagePhotoRecord> damagePhotos, DamageRecord? damage)
    {
        var snapshot = inspection.EquipmentSnapshot is { } s
            ? new EquipmentSnapshotRecord(s.AssetTag, s.EquipmentTypeName, s.Manufacturer, s.Model, s.SerialNumber, s.Processor, s.Memory, s.Storage)
            : null;

        return new ReturnInspectionRecord(
            inspection.Id, inspection.Condition, inspection.Notes, inspection.HasDamage, inspection.DamageDescription,
            inspection.InspectedByDisplayNameSnapshot, inspection.InspectedAtUtc, inspection.FinalizedAtUtc,
            snapshot, photos, damagePhotos, damage);
    }

    /// <summary>The group size is looked up so a return can say whether it is part of a multi-item collection worth linking back to.</summary>
    private async Task<EquipmentReturnDetailRecord> ToDetailRecordAsync(
        EquipmentReturn equipmentReturn, string assetTag, string equipmentTypeName, string consultantFullName, ReturnInspectionRecord? inspection,
        CancellationToken cancellationToken)
    {
        var returnGroupSize = equipmentReturn.ReturnGroupId is { } groupId
            ? await dbContext.EquipmentReturns.AsNoTracking().CountAsync(r => r.ReturnGroupId == groupId, cancellationToken)
            : 0;

        return new EquipmentReturnDetailRecord(
            equipmentReturn.Id, equipmentReturn.EquipmentAssignmentId, equipmentReturn.EquipmentId, assetTag, equipmentTypeName,
            equipmentReturn.ConsultantId, consultantFullName, equipmentReturn.Status, equipmentReturn.Reason, equipmentReturn.ExpectedReturnDate,
            equipmentReturn.ReceivedOn, equipmentReturn.ReceivedAtUtc, equipmentReturn.ReceivedByDisplayNameSnapshot,
            equipmentReturn.CreatedByUserId, equipmentReturn.CreatedByDisplayNameSnapshot, equipmentReturn.CreatedAtUtc, equipmentReturn.UpdatedAtUtc,
            equipmentReturn.CompletedAtUtc, equipmentReturn.CancelledAtUtc, equipmentReturn.CancelledByDisplayNameSnapshot,
            equipmentReturn.CancellationReason, inspection, equipmentReturn.ReturnGroupId, returnGroupSize);
    }
}
