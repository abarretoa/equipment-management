using System.Data;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EquipmentManagement.Infrastructure.Handovers;

/// <summary>
/// Registers that HR has physically shipped a Draft handover's equipment
/// ("Registar envio") and notifies the Consultant. Deliberately attaches to a
/// <em>Draft</em> handover, not a Finalized one: <see cref="EquipmentHandoverService.FinalizeAsync"/>
/// is the sole place custody (<c>EquipmentAssignment</c>) is created, so if
/// dispatch could only be registered after finalization, "shipped" would be
/// recorded after the system already considers the equipment delivered —
/// backwards from the real-world sequence (ship → arrive → finalize →
/// custody). Registering a dispatch moves every item from <c>Available</c> to
/// <c>InTransit</c>, but never creates an assignment (custody begins only at
/// Confirmar entrega) and never finalizes the handover.
///
/// Combines the persist-and-notify steps into one operation (unlike the
/// Responsibility Term's separate generate/send actions) because the product's
/// requirement is that "Registar envio" itself is what triggers the
/// notification — not a second, separately-permissioned send step. Follows
/// the same ADR-052 ordering discipline as
/// <c>ResponsibilityTermDeliveryService</c>: a durable
/// <see cref="DeliveryStatus.Attempting"/> row is inserted and committed
/// before the email is handed to <see cref="IEmailSender"/>; the dispatch
/// itself is already durably committed before that even begins, so email
/// failure can never roll back the dispatch or touch lifecycle/custody.
/// </summary>
public sealed class EquipmentDispatchService(
    ApplicationDbContext dbContext,
    AuditService auditService,
    IEmailSender emailSender,
    EmailFailureLogger emailFailureLogger,
    TimeProvider timeProvider,
    ILogger<EquipmentDispatchService> logger)
{
    public async Task<ServiceResult<EquipmentDispatchRecord>> RegisterAsync(
        Guid handoverId, RegisterDispatchData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var handover = await dbContext.EquipmentHandovers.AsNoTracking().FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
        if (handover is null)
        {
            return ServiceResult.Fail<EquipmentDispatchRecord>(ServiceErrorCode.NotFound, "Handover not found.");
        }

        if (handover.Status != HandoverStatus.Draft)
        {
            return ServiceResult.Fail<EquipmentDispatchRecord>(
                ServiceErrorCode.HandoverNotDraft, "O envio só pode ser registado enquanto o Handover está em rascunho.");
        }

        // Iteration 3: dispatch exists only for Carrier deliveries.
        // A legacy Draft with no method recorded must choose one first.
        if (handover.DeliveryMethod is null)
        {
            return ServiceResult.Fail<EquipmentDispatchRecord>(
                ServiceErrorCode.DeliveryMethodRequired, "Selecione o método de entrega antes de registar o envio.");
        }

        if (!HandoverRules.AllowsDispatch(handover.DeliveryMethod))
        {
            return ServiceResult.Fail<EquipmentDispatchRecord>(
                ServiceErrorCode.DispatchNotApplicable, "O registo de envio só se aplica a entregas por transportadora.");
        }

        var eligibility = await HandoverConsultantEligibility.ResolveAsync(dbContext, logger, handover.ConsultantId, cancellationToken);
        if (eligibility != ConsultantHandoverEligibility.Eligible)
        {
            var (code, message) = HandoverConsultantEligibility.ToFailure(eligibility);
            return ServiceResult.Fail<EquipmentDispatchRecord>(code, message);
        }

        var alreadyDispatched = await dbContext.EquipmentDispatches.AsNoTracking().Active().AnyAsync(d => d.EquipmentHandoverId == handoverId, cancellationToken);
        if (alreadyDispatched)
        {
            return ServiceResult.Fail<EquipmentDispatchRecord>(
                ServiceErrorCode.DispatchAlreadyRegistered, "Este Handover já tem um envio registado.");
        }

        var items = await (
            from i in dbContext.EquipmentHandoverItems.AsNoTracking()
            join e in dbContext.Equipment.AsNoTracking() on i.EquipmentId equals e.Id
            join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
            where i.EquipmentHandoverId == handoverId
            select new { e, t }).ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return ServiceResult.Fail<EquipmentDispatchRecord>(
                ServiceErrorCode.Validation, "Um Handover precisa de pelo menos um equipamento para poder ser enviado.");
        }

        if (data.ExpectedDeliveryToDate is { } to && to < data.ExpectedDeliveryFromDate)
        {
            return ServiceResult.Fail<EquipmentDispatchRecord>(
                ServiceErrorCode.Validation, "A data final da previsão de entrega não pode ser anterior à data inicial.");
        }

        var consultant = await dbContext.Consultants.AsNoTracking().FirstAsync(c => c.Id == handover.ConsultantId, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var dispatch = new EquipmentDispatch
        {
            Id = Guid.NewGuid(),
            EquipmentHandoverId = handoverId,
            ExpectedDeliveryFromDate = data.ExpectedDeliveryFromDate,
            ExpectedDeliveryToDate = data.ExpectedDeliveryToDate,
            DispatchedAtUtc = now,
            DispatchedByUserId = actorUserId,
            DispatchedByDisplayNameSnapshot = actorDisplayName,
        };

        var assetTags = items.Select(x => x.e.AssetTag).ToList();
        var equipmentIds = items.Select(x => x.e.Id).ToList();

        // The dispatch, the Available -> InTransit transition of every item
        // and the audit row commit together, before — never around — the
        // notification email below. Serializable, with the equipment re-read
        // inside the transaction: the same item may sit in several Drafts, and
        // a competing dispatch or "Confirmar entrega" that commits it first
        // either is seen here (not Available) or makes this commit fail with a
        // serialization conflict. Never a partial result.
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            try
            {
                var equipment = await dbContext.Equipment.Where(e => equipmentIds.Contains(e.Id)).ToListAsync(cancellationToken);
                var unavailable = equipment.Where(e => !EquipmentLifecycleRules.CanDispatch(e.Status)).Select(e => e.AssetTag).Order().ToList();
                if (unavailable.Count > 0)
                {
                    dbContext.ChangeTracker.Clear();
                    return ServiceResult.Fail<EquipmentDispatchRecord>(
                        ServiceErrorCode.InvalidLifecycleTransition, BuildUnavailableMessage(unavailable));
                }

                foreach (var e in equipment)
                {
                    e.Status = EquipmentStatus.InTransit;
                    e.UpdatedAtUtc = now;
                }

                dbContext.EquipmentDispatches.Add(dispatch);
                await dbContext.SaveChangesAsync(cancellationToken);

                await auditService.RecordAsync(new AuditEntry(
                    AuditActions.EquipmentDispatch.Registered,
                    EntityType: "EquipmentHandover",
                    EntityId: handoverId.ToString(),
                    ActorUserId: actorUserId,
                    ActorDisplayNameSnapshot: actorDisplayName,
                    NewValues: new
                    {
                        DispatchId = dispatch.Id,
                        ConsultantId = consultant.Id,
                        ConsultantFullName = consultant.FullName,
                        AssetTags = assetTags,
                        dispatch.ExpectedDeliveryFromDate,
                        dispatch.ExpectedDeliveryToDate,
                        EquipmentStatusFrom = nameof(EquipmentStatus.Available),
                        EquipmentStatusTo = nameof(EquipmentStatus.InTransit),
                    }), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
            {
                dbContext.ChangeTracker.Clear();
                return ServiceResult.Fail<EquipmentDispatchRecord>(
                    ServiceErrorCode.DispatchAlreadyRegistered, "Este Handover já tem um envio registado.");
            }
            catch (Exception ex) when (PostgresConflictClassifier.Find(ex, PostgresErrorCodes.ForeignKeyViolation) is
                { ConstraintName: DispatchHandoverForeignKey })
            {
                // "Eliminar rascunho" committed between the checks above and
                // this insert: the Draft no longer exists, so nothing was
                // registered. Only this one expected race is translated — any
                // other database failure still surfaces as an error.
                dbContext.ChangeTracker.Clear();
                return ServiceResult.Fail<EquipmentDispatchRecord>(
                    ServiceErrorCode.Concurrency, "Esta entrega já não está disponível para registar o envio. Atualize a página.");
            }
            catch (Exception ex) when (PostgresConflictClassifier.Find(ex, PostgresErrorCodes.SerializationFailure) is not null)
            {
                // Nothing here was committed. Under Serializable isolation a
                // concurrent "Eliminar rascunho" also surfaces as a
                // serialization conflict (not the foreign-key violation above),
                // so tell the two races apart by whether the Draft still exists.
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                var draftStillExists = await dbContext.EquipmentHandovers.AsNoTracking().AnyAsync(h => h.Id == handoverId, cancellationToken);
                return ServiceResult.Fail<EquipmentDispatchRecord>(
                    ServiceErrorCode.Concurrency,
                    draftStillExists
                        ? "Um dos equipamentos desta entrega foi enviado ou entregue por outra operação. Atualize a página e tente novamente."
                        : "Esta entrega já não está disponível para registar o envio. Atualize a página.");
            }
        }

        await SendNotificationAsync(dispatch, consultant.FullName, consultant.Email, items.Select(x => (x.e, x.t)).ToList(), cancellationToken);

        return ServiceResult.Ok(ToRecord(dispatch));
    }

    /// <summary>
    /// "Anular envio": cancels the active dispatch of a Carrier Draft before
    /// delivery is confirmed. As one unit, or not at all: the dispatch row is
    /// marked cancelled (never deleted), every shipped item returns from
    /// InTransit to Available, a current term prepared for this dispatch is
    /// superseded (its PDF, snapshot and photo-set links untouched: they belong
    /// to that version), and the Draft is back where it was before Registar envio.
    /// No Assignment is involved, and no email is sent. Everything is read
    /// inside one Serializable transaction, so a racing Confirmar entrega,
    /// dispatch, term generation or second cancellation either is seen here
    /// or makes one of the two fail with a conflict.
    /// </summary>
    public async Task<ServiceResult> CancelAsync(
        Guid handoverId, string? reason, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason))
        {
            return ServiceResult.Fail(ServiceErrorCode.Validation, "Indique o motivo da anulação.");
        }

        if (trimmedReason.Length > EquipmentDispatch.MaxCancellationReasonLength)
        {
            return ServiceResult.Fail(
                ServiceErrorCode.Validation, $"O motivo da anulação não pode ter mais de {EquipmentDispatch.MaxCancellationReasonLength} caracteres.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var handover = await dbContext.EquipmentHandovers.FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken);
            if (handover is null)
            {
                return ServiceResult.Fail(ServiceErrorCode.NotFound, "Handover not found.");
            }

            if (!HandoverRules.AllowsDispatch(handover.DeliveryMethod))
            {
                return ServiceResult.Fail(ServiceErrorCode.DispatchNotApplicable, "A anulação do envio só se aplica a entregas por transportadora.");
            }

            if (handover.Status != HandoverStatus.Draft)
            {
                return ServiceResult.Fail(
                    ServiceErrorCode.HandoverNotDraft,
                    "A entrega já foi confirmada e o envio já não pode ser anulado. Para reaver o equipamento, registe uma devolução.");
            }

            var dispatch = await dbContext.EquipmentDispatches.Active().FirstOrDefaultAsync(d => d.EquipmentHandoverId == handoverId, cancellationToken);
            if (dispatch is null || !HandoverRules.CanCancelDispatch(handover.Status, handover.DeliveryMethod, hasActiveDispatch: true))
            {
                return ServiceResult.Fail(ServiceErrorCode.DispatchRequired, "Esta entrega não tem um envio ativo para anular.");
            }

            var items = await dbContext.EquipmentHandoverItems.AsNoTracking().Where(i => i.EquipmentHandoverId == handoverId).ToListAsync(cancellationToken);
            var equipmentIds = items.Select(i => i.EquipmentId).ToList();
            var equipment = await dbContext.Equipment.Where(e => equipmentIds.Contains(e.Id)).ToListAsync(cancellationToken);

            // The whole dispatch or nothing: one item no longer InTransit (or
            // already in custody) refuses the cancellation for every item.
            var notInTransit = equipment.Where(e => !EquipmentLifecycleRules.CanCancelDispatch(e.Status)).Select(e => e.AssetTag).Order().ToList();
            var inCustody = await dbContext.EquipmentAssignments.AnyAsync(
                a => a.Status == AssignmentStatus.Active && (equipmentIds.Contains(a.EquipmentId) || a.EquipmentHandoverId == handoverId), cancellationToken);
            if (notInTransit.Count > 0 || inCustody)
            {
                return ServiceResult.Fail(
                    ServiceErrorCode.InvalidLifecycleTransition,
                    "O envio já não pode ser anulado: nem todos os equipamentos desta entrega estão em trânsito.");
            }

            // Fail-safe: a signed copy is impossible before delivery today, so
            // one here is inconsistent data; never repaired by this operation.
            var termIds = await dbContext.ResponsibilityTerms.Where(t => t.EquipmentHandoverId == handoverId).Select(t => t.Id).ToListAsync(cancellationToken);
            if (await dbContext.ResponsibilityTermSignedArtifacts.AnyAsync(s => termIds.Contains(s.ResponsibilityTermId), cancellationToken))
            {
                return ServiceResult.Fail(
                    ServiceErrorCode.InvalidLifecycleTransition, "O envio não pode ser anulado: esta entrega já tem um termo assinado.");
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;

            dispatch.CancelledAtUtc = now;
            dispatch.CancelledByUserId = actorUserId;
            dispatch.CancelledByDisplayNameSnapshot = actorDisplayName;
            dispatch.CancellationReason = trimmedReason;

            foreach (var e in equipment)
            {
                e.Status = EquipmentStatus.Available;
                e.UpdatedAtUtc = now;
            }

            var currentTerm = await dbContext.ResponsibilityTerms.FirstOrDefaultAsync(
                t => t.EquipmentHandoverId == handoverId && t.Status == ResponsibilityTermStatus.Generated, cancellationToken);
            if (currentTerm is not null)
            {
                currentTerm.Status = ResponsibilityTermStatus.Superseded;
                currentTerm.UpdatedAtUtc = now;
            }

            // The superseded version keeps its own frozen snapshot and photo
            // links untouched (they belong to that version, not the Handover).
            // A Draft has no delivery record yet, so there is nothing else to
            // reset: the next version or Confirmar entrega freezes the
            // then-current data. Touching the row makes racing operations
            // on this Handover conflict.
            handover.UpdatedAtUtc = now;

            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentDispatch.Cancelled,
                EntityType: "EquipmentHandover",
                EntityId: handoverId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                OldValues: new { DispatchId = dispatch.Id, dispatch.DispatchedAtUtc, dispatch.DispatchedByUserId },
                NewValues: new
                {
                    DispatchId = dispatch.Id,
                    Reason = trimmedReason,
                    AssetTags = equipment.Select(e => e.AssetTag).Order().ToList(),
                    EquipmentStatusFrom = nameof(EquipmentStatus.InTransit),
                    EquipmentStatusTo = nameof(EquipmentStatus.Available),
                    SupersededTermId = currentTerm?.Id,
                    SupersededTermVersion = currentTerm?.Version,
                }), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return ServiceResult.Ok();
        }
        catch (Exception ex) when (PostgresConflictClassifier.Find(
            ex, PostgresErrorCodes.SerializationFailure, PostgresErrorCodes.UniqueViolation) is not null)
        {
            // A concurrent Confirmar entrega, dispatch, term generation or
            // cancellation committed first; nothing here was committed.
            dbContext.ChangeTracker.Clear();
            return ServiceResult.Fail(
                ServiceErrorCode.Concurrency, "Esta entrega foi alterada por outra operação. Atualize a página e tente novamente.");
        }
    }

    private async Task SendNotificationAsync(
        EquipmentDispatch dispatch, string consultantFullName, string consultantEmail,
        IReadOnlyList<(Equipment Equipment, EquipmentType Type)> items, CancellationToken cancellationToken)
    {
        if (!ResponsibilityTermDeliveryRules.IsValidEmail(consultantEmail))
        {
            // No valid address to notify — nothing to attempt or record;
            // dispatch itself remains registered regardless.
            return;
        }

        var mobileImeiByEquipmentId = await BuildMobileImeiLookupAsync(items, cancellationToken);
        var lines = items.Select(x => new DispatchEquipmentLine(
            x.Equipment.AssetTag, x.Type.Name, x.Equipment.Manufacturer, x.Equipment.Model, x.Equipment.SerialNumber,
            mobileImeiByEquipmentId.GetValueOrDefault(x.Equipment.Id))).ToList();

        var (subject, body) = EquipmentDispatchEmailTemplate.Build(
            consultantFullName, lines, dispatch.ExpectedDeliveryFromDate, dispatch.ExpectedDeliveryToDate);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var notification = new EquipmentDispatchNotification
        {
            Id = Guid.NewGuid(),
            EquipmentDispatchId = dispatch.Id,
            RecipientEmailSnapshot = consultantEmail,
            Status = DeliveryStatus.Attempting,
            AttemptedAtUtc = now,
            CreatedAtUtc = now,
        };

        // Durable-attempt-before-side-effect invariant (ADR-052) — see
        // ResponsibilityTermDeliveryService.SendAsync's remarks.
        dbContext.EquipmentDispatchNotifications.Add(notification);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAsync(new EmailMessage(consultantEmail, subject, body, IncludeSignature: true), cancellationToken);
        }
        catch (EmailSendException ex)
        {
            emailFailureLogger.DeliveryFailed(EmailMessageTypes.DispatchNotification, notification.Id, ex);

            notification.Status = DeliveryStatus.Failed;
            notification.FailureCode = ToDeliveryFailureCode(ex.Reason);

            // The send already happened (outside any transaction); only the
            // recorded outcome and its audit row commit together.
            await using var failedTransaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentDispatch.NotificationFailed,
                EntityType: "EquipmentHandover",
                EntityId: dispatch.EquipmentHandoverId.ToString(),
                ActorUserId: dispatch.DispatchedByUserId,
                ActorDisplayNameSnapshot: dispatch.DispatchedByDisplayNameSnapshot,
                NewValues: new { DispatchId = dispatch.Id, NotificationId = notification.Id, FailureCode = notification.FailureCode.ToString() }),
                cancellationToken);
            await failedTransaction.CommitIfOwnedAsync(cancellationToken);
            return;
        }

        notification.Status = DeliveryStatus.Sent;
        notification.SentAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var sentTransaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.EquipmentDispatch.NotificationSent,
            EntityType: "EquipmentHandover",
            EntityId: dispatch.EquipmentHandoverId.ToString(),
            ActorUserId: dispatch.DispatchedByUserId,
            ActorDisplayNameSnapshot: dispatch.DispatchedByDisplayNameSnapshot,
            NewValues: new { DispatchId = dispatch.Id, NotificationId = notification.Id }), cancellationToken);
        await sentTransaction.CommitIfOwnedAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, string?>> BuildMobileImeiLookupAsync(
        IReadOnlyList<(Equipment Equipment, EquipmentType Type)> items, CancellationToken cancellationToken)
    {
        var mobileEquipmentIds = items.Where(x => x.Type.Kind == EquipmentTypeKind.MobilePhone).Select(x => x.Equipment.Id).ToList();
        if (mobileEquipmentIds.Count == 0)
        {
            return [];
        }

        return await dbContext.MobilePhoneSpecifications.AsNoTracking()
            .Where(s => mobileEquipmentIds.Contains(s.EquipmentId))
            .ToDictionaryAsync(s => s.EquipmentId, s => (string?)s.Imei, cancellationToken);
    }

    private const string DispatchHandoverForeignKey = "FK_EquipmentDispatches_EquipmentHandovers_EquipmentHandoverId";

    private static string BuildUnavailableMessage(List<string> assetTags) => assetTags.Count == 1
        ? $"O equipamento {assetTags[0]} já não está disponível e não pode ser enviado. Remova-o desta entrega para continuar."
        : $"Os equipamentos {string.Join(", ", assetTags)} já não estão disponíveis e não podem ser enviados. Remova-os desta entrega para continuar.";

    private static DeliveryFailureCode ToDeliveryFailureCode(EmailSendFailureReason reason) => reason switch
    {
        EmailSendFailureReason.NotConfigured => DeliveryFailureCode.ConfigurationError,
        EmailSendFailureReason.Rejected => DeliveryFailureCode.ProviderRejected,
        EmailSendFailureReason.Temporary => DeliveryFailureCode.TemporaryFailure,
        _ => DeliveryFailureCode.Unknown,
    };

    private static EquipmentDispatchRecord ToRecord(EquipmentDispatch d) => new(
        d.Id, d.EquipmentHandoverId, d.ExpectedDeliveryFromDate, d.ExpectedDeliveryToDate, d.DispatchedAtUtc, d.DispatchedByDisplayNameSnapshot);
}
