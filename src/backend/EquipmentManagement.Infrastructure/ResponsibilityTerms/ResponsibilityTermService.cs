using System.Data;
using System.Security.Cryptography;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Documents;
using EquipmentManagement.Infrastructure.Files;
using EquipmentManagement.Infrastructure.Handovers;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EquipmentManagement.Infrastructure.ResponsibilityTerms;

/// <summary>
/// Generates and serves the Responsibility Term PDF for a finalized
/// <see cref="EquipmentHandover"/> (docs/PROJECT_SPEC.md, section 11; Phase 6 scope). <see cref="GenerateAsync"/> is the only method that can ever create
/// a <see cref="ResponsibilityTerm"/> row or move one from <c>Generated</c> to
/// <c>Superseded</c>. Every version owns the exact frozen snapshot it was
/// rendered from (<see cref="ResponsibilityTerm.ConsultantSnapshot"/>,
/// <see cref="ResponsibilityTerm.AllocationSnapshot"/> and its
/// <see cref="ResponsibilityTermItem"/> rows with their photo-set links),
/// captured once and never changed by a later version, a cancellation or the
/// Handover — so a historical PDF and its structured data never diverge
/// (docs/PROJECT_SPEC.md, section 11: "O PDF histórico não pode mudar se o
/// consultor ou equipamento for editado posteriormente").
/// </summary>
public sealed class ResponsibilityTermService(
    ApplicationDbContext dbContext,
    AuditService auditService,
    IPrivateFileStorage fileStorage,
    IResponsibilityTermPdfGenerator pdfGenerator,
    DocumentNumberGenerator documentNumberGenerator,
    IOptions<DocumentIssuerOptions> issuerOptions,
    TimeProvider timeProvider,
    ILogger<ResponsibilityTermService> logger)
{
    public async Task<IReadOnlyList<ResponsibilityTermRecord>> ListAsync(Guid handoverId, CancellationToken cancellationToken = default)
    {
        var terms = await dbContext.ResponsibilityTerms.AsNoTracking()
            .Where(t => t.EquipmentHandoverId == handoverId)
            .OrderByDescending(t => t.Version)
            .ToListAsync(cancellationToken);

        return terms.Select(ToRecord).ToList();
    }

    public async Task<ResponsibilityTermRecord?> GetByIdAsync(Guid termId, CancellationToken cancellationToken = default)
    {
        var term = await dbContext.ResponsibilityTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);
        return term is null ? null : ToRecord(term);
    }

    /// <summary>Streams the stored PDF and audits the download — the document contains Consultant PII, so knowing who accessed it (not just who generated it) is deliberately recorded (Phase 6 scope).</summary>
    public async Task<(Stream Content, string ContentType, string FileName)?> GetContentAsync(
        Guid termId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var term = await dbContext.ResponsibilityTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);
        if (term is null)
        {
            return null;
        }

        var stream = await fileStorage.OpenReadAsync(term.StorageKey, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.ResponsibilityTerm.Downloaded,
            EntityType: "ResponsibilityTerm",
            EntityId: term.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { term.DocumentNumber, term.Version, EquipmentHandoverId = term.EquipmentHandoverId }), cancellationToken);

        return (stream, term.ContentType, term.FileName);
    }

    /// <summary>
    /// Generates a new version. Works identically whether this is the
    /// handover's first term or a regeneration — the same operation, the same
    /// permission (Phase 6 scope) — except a regeneration also
    /// supersedes the previous current version, atomically with the new
    /// insert (Phase 6 scope).
    /// </summary>
    public async Task<ServiceResult<ResponsibilityTermRecord>> GenerateAsync(
        Guid handoverId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var handover = await dbContext.EquipmentHandovers.AsNoTracking().FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
        if (handover is null)
        {
            return ServiceResult.Fail<ResponsibilityTermRecord>(ServiceErrorCode.NotFound, "Handover not found.");
        }

        var hasDispatch = await dbContext.EquipmentDispatches.AsNoTracking().Active().AnyAsync(d => d.EquipmentHandoverId == handoverId, cancellationToken);
        if (!ResponsibilityTermRules.CanGenerate(handover.Status, handover.DeliveryMethod, hasDispatch))
        {
            return ServiceResult.Fail<ResponsibilityTermRecord>(ServiceErrorCode.HandoverNotFinalized, NotYetGeneratableMessage);
        }

        var preparingBeforeDelivery = ResponsibilityTermRules.CanPrepareBeforeDelivery(handover.Status, handover.DeliveryMethod, hasDispatch);

        var items = await dbContext.EquipmentHandoverItems.AsNoTracking()
            .Where(i => i.EquipmentHandoverId == handoverId)
            .OrderBy(i => i.CreatedAtUtc).ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return ServiceResult.Fail<ResponsibilityTermRecord>(
                ServiceErrorCode.SnapshotIncomplete, "This handover has no items and cannot produce a Responsibility Term.");
        }

        // The frozen data of THIS version, captured exactly once. The PDF is
        // rendered from it and the transaction below persists these very
        // values as the version's own snapshot, so document and database
        // always describe the same state.
        //   - Carrier term prepared after "Registar envio": captured now, from
        //     live data (nothing has been delivered yet).
        //   - After Confirmar entrega: copied from the Handover's delivery
        //     record, which was frozen once and never changes, so every
        //     post-delivery version describes the delivery itself.
        var frozenResult = preparingBeforeDelivery
            ? await CaptureForPreparationAsync(handover, items, cancellationToken)
            : FromDeliveryRecord(handover, items);
        if (!frozenResult.Succeeded)
        {
            return ServiceResult.Fail<ResponsibilityTermRecord>(frozenResult.ErrorCode, frozenResult.ErrorMessage!);
        }

        var frozen = frozenResult.Value!;

        // Photos are loaded by the exact EquipmentPhotoSetId frozen for this
        // version, never by "whichever set is Current now". A Handover
        // delivered before photos existed has no such reference, and
        // generation fails honestly rather than attach an unrelated set.
        var photoSetsByEquipmentId = new Dictionary<Guid, ResponsibilityTermPhotoSet>();
        foreach (var item in frozen.Items)
        {
            var kind = Enum.Parse<EquipmentTypeKind>(item.Equipment.Kind ?? nameof(EquipmentTypeKind.Generic));
            if (!EquipmentRules.RequiresConditionPhotos(kind))
            {
                continue;
            }

            if (item.PhotoSetId is not { } photoSetId)
            {
                return ServiceResult.Fail<ResponsibilityTermRecord>(
                    ServiceErrorCode.MissingCurrentPhotoSet,
                    $"O equipamento '{item.Equipment.AssetTag}' foi entregue antes da introdução do registo fotográfico e não tem um conjunto de fotografias associado a este Handover; não é possível gerar um Termo de Responsabilidade fotográfico para este registo histórico.");
            }

            var photoSet = await dbContext.EquipmentPhotoSets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == photoSetId, cancellationToken);
            var photoRows = await dbContext.EquipmentPhotoSetPhotos.AsNoTracking()
                .Where(p => p.EquipmentPhotoSetId == photoSetId)
                .OrderBy(p => p.SortOrder)
                .ToListAsync(cancellationToken);

            if (photoSet is null || photoRows.Count == 0)
            {
                return ServiceResult.Fail<ResponsibilityTermRecord>(
                    ServiceErrorCode.MissingCurrentPhotoSet,
                    $"O conjunto de fotografias do equipamento '{item.Equipment.AssetTag}' não foi encontrado.");
            }

            var photoBytesList = new List<byte[]>();
            foreach (var photoRow in photoRows)
            {
                await using var stream = await fileStorage.OpenReadAsync(photoRow.StorageKey, cancellationToken);
                if (stream is null)
                {
                    return ServiceResult.Fail<ResponsibilityTermRecord>(
                        ServiceErrorCode.MissingCurrentPhotoSet,
                        $"Uma das fotografias do equipamento '{item.Equipment.AssetTag}' não foi encontrada no armazenamento.");
                }

                await using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                photoBytesList.Add(buffer.ToArray());
            }

            photoSetsByEquipmentId[item.EquipmentId] = new ResponsibilityTermPhotoSet(photoSet.CreatedAtUtc, photoBytesList);
        }

        if (!issuerOptions.Value.IsConfigured)
        {
            // Deliberately an exception, not a ServiceResult failure: this is
            // a server misconfiguration, not a caller/domain-state problem —
            // it becomes a safe generic 500 via the existing GlobalExceptionHandler
            // (Phase 6 scope: fail clearly rather than embed a
            // fabricated/blank issuer into a legal document).
            throw new InvalidOperationException(
                "Document issuer is not configured (Documents:Issuer:LegalName/Address/TaxId) — Responsibility Term generation cannot proceed.");
        }

        // Steps below follow the ordering ADR-049 documents: build the
        // model, render, hash, and store the PDF entirely before opening any
        // database transaction — PostgreSQL and private file storage cannot
        // share one ACID transaction, so the transaction covers only the
        // metadata write (Phase 4/5's established storage-compensation
        // pattern, ADR-033).
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var documentNumber = await documentNumberGenerator.GenerateAsync(cancellationToken);
        var nextVersion = await GetNextVersionAsync(handoverId, cancellationToken);

        var model = BuildDocumentModel(handover, frozen, photoSetsByEquipmentId, documentNumber, nextVersion, actorDisplayName, now);
        var pdfBytes = pdfGenerator.Generate(model, issuerOptions.Value);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(pdfBytes));
        var storageKey = BuildStorageKey(handoverId);
        var fileName = BuildFileName(documentNumber, nextVersion);

        await using (var buffer = new MemoryStream(pdfBytes))
        {
            await fileStorage.SaveAsync(storageKey, buffer, cancellationToken);
        }

        // Every row this transaction mutates is loaded after it opens (not
        // before) — the same discipline established for EquipmentReturnService
        // (ADR-042): the read that decides "is there a current version to
        // supersede, and is version N still free" must itself be inside the
        // serializable snapshot for Postgres to detect a genuine concurrent
        // double-generation, and the fresh-insert unique indexes
        // (DocumentNumber; EquipmentHandoverId+Version) are the backstop for
        // the very first generation, which has no existing row to conflict on.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var reloadedHandover = await dbContext.EquipmentHandovers.FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
        var reloadedHasDispatch = await dbContext.EquipmentDispatches.AsNoTracking().Active().AnyAsync(d => d.EquipmentHandoverId == handoverId, cancellationToken);
        if (reloadedHandover is null || !ResponsibilityTermRules.CanGenerate(reloadedHandover.Status, reloadedHandover.DeliveryMethod, reloadedHasDispatch))
        {
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
            return ServiceResult.Fail<ResponsibilityTermRecord>(ServiceErrorCode.HandoverNotFinalized, NotYetGeneratableMessage);
        }

        if (preparingBeforeDelivery != ResponsibilityTermRules.CanPrepareBeforeDelivery(
                reloadedHandover.Status, reloadedHandover.DeliveryMethod, reloadedHasDispatch))
        {
            // "Confirmar entrega" committed while this PDF was being rendered
            // from freshly captured data: the handover now has its own frozen
            // snapshots, which this document was not built from. Discard it;
            // a retry renders from the finalized snapshots.
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
            return ServiceResult.Fail<ResponsibilityTermRecord>(
                ServiceErrorCode.Concurrency, "Esta entrega foi alterada por outra operação. Atualize a página e tente novamente.");
        }

        if (preparingBeforeDelivery)
        {
            // The composition this version froze must still be the Draft's
            // (dispatch locks it, but a cancellation can unlock it). Touching
            // the handover row also makes a racing Confirmar entrega or
            // "Anular envio" (both write it) conflict with this generation.
            var currentEquipmentIds = await dbContext.EquipmentHandoverItems.AsNoTracking()
                .Where(i => i.EquipmentHandoverId == handoverId).Select(i => i.EquipmentId).ToListAsync(cancellationToken);
            if (!currentEquipmentIds.Order().SequenceEqual(frozen.Items.Select(i => i.EquipmentId).Order()))
            {
                await fileStorage.DeleteAsync(storageKey, cancellationToken);
                return ServiceResult.Fail<ResponsibilityTermRecord>(
                    ServiceErrorCode.Concurrency, "Esta entrega foi alterada por outra operação. Atualize a página e tente novamente.");
            }

            reloadedHandover.UpdatedAtUtc = now;
        }

        var currentTerm = await dbContext.ResponsibilityTerms
            .FirstOrDefaultAsync(t => t.EquipmentHandoverId == handoverId && t.Status == ResponsibilityTermStatus.Generated, cancellationToken);

        if (currentTerm is not null)
        {
            currentTerm.Status = ResponsibilityTermStatus.Superseded;
            currentTerm.UpdatedAtUtc = now;
        }

        var term = new ResponsibilityTerm
        {
            Id = Guid.NewGuid(),
            EquipmentHandoverId = handoverId,
            DocumentNumber = documentNumber,
            Version = nextVersion,
            Status = ResponsibilityTermStatus.Generated,
            TemplateVersion = model.TemplateVersion,
            GeneratedAtUtc = now,
            GeneratedByUserId = actorUserId,
            GeneratedByDisplayNameSnapshot = actorDisplayName,
            StorageKey = storageKey,
            FileName = fileName,
            ContentType = "application/pdf",
            SizeBytes = pdfBytes.LongLength,
            Sha256 = sha256,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,

            // This version's own frozen snapshot: the exact values rendered
            // above, inserted with the term in the same SaveChanges, so a
            // Generated version never exists without it.
            ConsultantSnapshot = frozen.Consultant,
            AllocationSnapshot = frozen.Allocation,
            SnapshotUnavailable = false,
        };

        dbContext.ResponsibilityTerms.Add(term);
        dbContext.ResponsibilityTermItems.AddRange(frozen.Items.Select((item, index) => new ResponsibilityTermItem
        {
            Id = Guid.NewGuid(),
            ResponsibilityTermId = term.Id,
            EquipmentId = item.EquipmentId,
            SortOrder = index,
            EquipmentSnapshot = item.Equipment,
            EquipmentPhotoSetId = item.PhotoSetId,
        }));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ResponsibilityTerm.Generated,
                EntityType: "ResponsibilityTerm",
                EntityId: term.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { term.DocumentNumber, term.Version, EquipmentHandoverId = handoverId }), cancellationToken);

            if (currentTerm is not null)
            {
                await auditService.RecordAsync(new AuditEntry(
                    AuditActions.ResponsibilityTerm.Superseded,
                    EntityType: "ResponsibilityTerm",
                    EntityId: currentTerm.Id.ToString(),
                    ActorUserId: actorUserId,
                    ActorDisplayNameSnapshot: actorDisplayName,
                    NewValues: new { currentTerm.DocumentNumber, currentTerm.Version, SupersededByVersion = term.Version }), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.Find(ex, PostgresErrorCodes.UniqueViolation, PostgresErrorCodes.SerializationFailure) is not null)
        {
            // A concurrent request already generated a version for this
            // handover between our reads and this insert — clean up the PDF
            // we already wrote so it never becomes an orphan (ADR-033's
            // established compensation pattern), and ask the caller to retry
            // (it will see the now-current version and compute a fresh one).
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
            return ServiceResult.Fail<ResponsibilityTermRecord>(
                ServiceErrorCode.Concurrency, "Another Responsibility Term generation is already in progress for this handover; please retry.");
        }

        return ServiceResult.Ok(ToRecord(term));
    }

    private const string NotYetGeneratableMessage =
        "O termo de responsabilidade só pode ser gerado depois de confirmada a entrega ou, numa entrega por transportadora, depois de registado o envio.";

    /// <summary>The data one term version renders and owns, captured once (see <see cref="GenerateAsync"/>).</summary>
    private sealed record FrozenTerm(ConsultantSnapshot Consultant, AllocationSnapshot? Allocation, IReadOnlyList<FrozenTermItem> Items);

    /// <summary>One item of a <see cref="FrozenTerm"/>, in document order.</summary>
    private sealed record FrozenTermItem(Guid EquipmentId, EquipmentSnapshot Equipment, Guid? PhotoSetId);

    /// <summary>
    /// After Confirmar entrega: the version copies the Handover's delivery
    /// record (frozen once at finalization and never changed), as fresh
    /// values owned by the version alone. A legacy/corrupted row without a
    /// delivery record fails safely, never falling back to live data (Phase 6 scope).
    /// </summary>
    private static ServiceResult<FrozenTerm> FromDeliveryRecord(EquipmentHandover handover, List<EquipmentHandoverItem> items)
    {
        if (handover.ConsultantSnapshot is null)
        {
            return ServiceResult.Fail<FrozenTerm>(
                ServiceErrorCode.SnapshotIncomplete, "This handover has no consultant snapshot and cannot produce a Responsibility Term.");
        }

        if (items.Any(i => i.EquipmentSnapshot is null))
        {
            return ServiceResult.Fail<FrozenTerm>(
                ServiceErrorCode.SnapshotIncomplete, "One or more items on this handover has no equipment snapshot recorded.");
        }

        return ServiceResult.Ok(new FrozenTerm(
            HandoverSnapshotFactory.Copy(handover.ConsultantSnapshot),
            HandoverSnapshotFactory.Copy(handover.AllocationSnapshot),
            items.Select(i => new FrozenTermItem(i.EquipmentId, HandoverSnapshotFactory.Copy(i.EquipmentSnapshot!), i.EquipmentPhotoSetId)).ToList()));
    }

    /// <summary>
    /// For a carrier term prepared after "Registar envio": validates the same
    /// prerequisites "Confirmar entrega" enforces for the frozen data (an
    /// active, eligible Consultant; every item still InTransit on this
    /// delivery with its delivery inspection recorded; Laptop/Mobile Phone
    /// items with a Current photo set), then captures this version's snapshot
    /// from live data through the same <see cref="HandoverSnapshotFactory"/>
    /// FinalizeAsync uses. Nothing is written to the Handover: the snapshot
    /// belongs to the version, and nothing about custody changes (no
    /// Assignment; the equipment stays InTransit).
    /// </summary>
    private async Task<ServiceResult<FrozenTerm>> CaptureForPreparationAsync(
        EquipmentHandover handover, List<EquipmentHandoverItem> items, CancellationToken cancellationToken)
    {
        var consultant = await dbContext.Consultants.AsNoTracking().FirstAsync(c => c.Id == handover.ConsultantId, cancellationToken);
        if (!consultant.IsActive)
        {
            return ServiceResult.Fail<FrozenTerm>(ServiceErrorCode.InactiveReference, "Um consultor inativo não pode receber equipamento.");
        }

        var eligibility = await HandoverConsultantEligibility.ResolveAsync(dbContext, logger, consultant.Id, cancellationToken);
        if (eligibility != ConsultantHandoverEligibility.Eligible)
        {
            var (code, message) = HandoverConsultantEligibility.ToFailure(eligibility);
            return ServiceResult.Fail<FrozenTerm>(code, message);
        }

        var allocation = await dbContext.ConsultantAllocations.AsNoTracking()
            .FirstAsync(a => a.ConsultantId == consultant.Id && a.Status == AllocationStatus.Active, cancellationToken);
        var companyName = allocation.CompanyId is { } companyId
            ? await dbContext.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        var missingPhotoAssetTags = new List<string>();
        var frozenItems = new List<FrozenTermItem>(items.Count);
        foreach (var item in items)
        {
            var equipment = await dbContext.Equipment.AsNoTracking().FirstOrDefaultAsync(e => e.Id == item.EquipmentId, cancellationToken);
            if (equipment is null)
            {
                return ServiceResult.Fail<FrozenTerm>(ServiceErrorCode.NotFound, "Equipamento não encontrado.");
            }

            // Dispatch moved every item of this Draft to InTransit; anything
            // else means the item left this delivery.
            if (equipment.Status != EquipmentLifecycleRules.RequiredStatusForDeliveryConfirmation(isCarrier: true))
            {
                return ServiceResult.Fail<FrozenTerm>(
                    ServiceErrorCode.InvalidLifecycleTransition,
                    $"O equipamento {equipment.AssetTag} já não está disponível e não pode ser incluído no termo.");
            }

            if (!await dbContext.DeliveryInspections.AsNoTracking().AnyAsync(i => i.EquipmentHandoverItemId == item.Id, cancellationToken))
            {
                return ServiceResult.Fail<FrozenTerm>(
                    ServiceErrorCode.InspectionMissing, $"O equipamento {equipment.AssetTag} não tem a inspeção de entrega registada.");
            }

            var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);
            var spec = type.Kind == EquipmentTypeKind.Laptop
                ? await dbContext.LaptopSpecifications.AsNoTracking().FirstOrDefaultAsync(s => s.EquipmentId == equipment.Id, cancellationToken)
                : null;
            var mobileSpec = type.Kind == EquipmentTypeKind.MobilePhone
                ? await dbContext.MobilePhoneSpecifications.AsNoTracking().FirstOrDefaultAsync(s => s.EquipmentId == equipment.Id, cancellationToken)
                : null;

            EquipmentPhotoSet? photoSet = null;
            if (EquipmentRules.RequiresConditionPhotos(type.Kind))
            {
                photoSet = await dbContext.EquipmentPhotoSets.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.EquipmentId == equipment.Id && s.Status == PhotoSetStatus.Current, cancellationToken);
                if (photoSet is null)
                {
                    missingPhotoAssetTags.Add(equipment.AssetTag);
                }
            }

            frozenItems.Add(new FrozenTermItem(equipment.Id, HandoverSnapshotFactory.ForEquipment(equipment, type, spec, mobileSpec), photoSet?.Id));
        }

        if (missingPhotoAssetTags.Count > 0)
        {
            return ServiceResult.Fail<FrozenTerm>(
                ServiceErrorCode.MissingCurrentPhotoSet,
                missingPhotoAssetTags.Count == 1
                    ? $"Não é possível gerar o termo.\nO equipamento {missingPhotoAssetTags[0]} não tem fotografias atuais registadas. Registe as 3 fotografias do equipamento antes de gerar o termo."
                    : $"Não é possível gerar o termo.\nOs equipamentos {string.Join(", ", missingPhotoAssetTags)} não têm fotografias atuais registadas. Registe as 3 fotografias de cada equipamento antes de gerar o termo.");
        }

        return ServiceResult.Ok(new FrozenTerm(
            HandoverSnapshotFactory.ForConsultant(consultant),
            HandoverSnapshotFactory.ForAllocation(allocation, companyName),
            frozenItems));
    }

    /// <summary>
    /// A best-effort read, not inside any transaction — a genuine race is
    /// still caught by the unique <c>(EquipmentHandoverId, Version)</c> index
    /// at insert time (see <see cref="GenerateAsync"/>'s catch clause), so
    /// this never needs to be more than "usually correct."
    /// </summary>
    private async Task<int> GetNextVersionAsync(Guid handoverId, CancellationToken cancellationToken)
    {
        var maxVersion = await dbContext.ResponsibilityTerms.AsNoTracking()
            .Where(t => t.EquipmentHandoverId == handoverId)
            .Select(t => (int?)t.Version)
            .MaxAsync(cancellationToken);

        return (maxVersion ?? 0) + 1;
    }

    /// <summary>Renders exclusively from the version's <paramref name="frozen"/> snapshot — the same values that version persists.</summary>
    private static ResponsibilityTermDocumentModel BuildDocumentModel(
        EquipmentHandover handover, FrozenTerm frozen, IReadOnlyDictionary<Guid, ResponsibilityTermPhotoSet> photoSetsByEquipmentId,
        string documentNumber, int version, string generatedByDisplayName, DateTime generatedAtUtc)
    {
        var consultantSnapshot = frozen.Consultant;
        var consultant = new ResponsibilityTermConsultantInfo(
            consultantSnapshot.FullName, consultantSnapshot.Nif, consultantSnapshot.Address, consultantSnapshot.Phone, consultantSnapshot.Email);

        var professionalContext = frozen.Allocation is { } allocation
            ? new ResponsibilityTermProfessionalContext(allocation.WorkforceType, allocation.EngagementType, allocation.RoleOrFunction, allocation.CompanyName)
            : null;

        var equipmentItems = frozen.Items.Select(i =>
        {
            var snapshot = i.Equipment;
            var laptopSpecification = snapshot.Processor is not null
                ? new ResponsibilityTermLaptopSpecification(snapshot.Processor, snapshot.Memory ?? string.Empty, snapshot.Storage ?? string.Empty)
                : null;
            var mobileSpecification = snapshot.Imei is not null
                ? new ResponsibilityTermMobilePhoneSpecification(snapshot.Imei, snapshot.MobileNumber, snapshot.SimSerialNumber)
                : null;
            var photoSet = photoSetsByEquipmentId.GetValueOrDefault(i.EquipmentId);

            return new ResponsibilityTermEquipmentItem(
                snapshot.AssetTag, snapshot.EquipmentTypeName, snapshot.Manufacturer, snapshot.Model, snapshot.SerialNumber,
                snapshot.Kind ?? nameof(EquipmentTypeKind.Generic), laptopSpecification, mobileSpecification, snapshot.Notes, photoSet);
        }).ToList();

        return new ResponsibilityTermDocumentModel(
            documentNumber,
            version,
            ResponsibilityTermTemplate.CurrentVersion,
            handover.DeliveredOn,
            handover.CreatedByDisplayNameSnapshot,
            generatedAtUtc,
            generatedByDisplayName,
            consultant,
            professionalContext,
            equipmentItems);
    }

    private static string BuildStorageKey(Guid handoverId) => $"responsibility-terms/{handoverId:N}/{Guid.NewGuid():N}.pdf";

    private static string BuildFileName(string documentNumber, int version) => $"responsibility-term-{documentNumber}-v{version}.pdf";

    private static ResponsibilityTermRecord ToRecord(ResponsibilityTerm term) => new(
        term.Id, term.EquipmentHandoverId, term.DocumentNumber, term.Version, term.Status, term.TemplateVersion,
        term.GeneratedAtUtc, term.GeneratedByDisplayNameSnapshot, term.FileName, term.ContentType, term.SizeBytes,
        term.Sha256, term.CreatedAtUtc, term.UpdatedAtUtc);
}
