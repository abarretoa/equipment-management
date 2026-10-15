using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Files;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.ResponsibilityTerms;

/// <summary>
/// Sends the current, immutable Responsibility Term PDF to the consultant for
/// external signature, and records every send attempt (Phase 7 scope). This is the only place that creates a
/// <see cref="ResponsibilityTermDelivery"/> row.
///
/// Ordering discipline (Phase 7 hardening pass; ADR-052 update): a durable
/// <see cref="DeliveryStatus.Attempting"/> row is inserted and committed
/// *before* the email is handed to <see cref="IEmailSender"/> — never the
/// other way around. PostgreSQL and an SMTP send cannot share one
/// transaction, so the invariant this ordering buys is: if the application
/// cannot durably record that it is about to attempt a send, it must not
/// attempt one. The row is then updated exactly once to its terminal
/// <c>Sent</c>/<c>Failed</c> state once the mail transport call returns. The
/// one residual ambiguity this cannot remove — the mail transport succeeds
/// but the terminal update itself cannot be persisted (a database outage in
/// that exact window) — leaves the row <c>Attempting</c> rather than
/// fabricating a <c>Sent</c> or <c>Failed</c> outcome the application does
/// not actually know; see ADR-052. No outbox/queue is used — see ADR-052 for
/// why one isn't justified for this internal, low-volume, human-triggered
/// workflow.
/// </summary>
public sealed class ResponsibilityTermDeliveryService(
    ApplicationDbContext dbContext,
    AuditService auditService,
    IPrivateFileStorage fileStorage,
    IEmailSender emailSender,
    EmailFailureLogger emailFailureLogger,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<ResponsibilityTermDeliveryRecord>> ListAsync(Guid termId, CancellationToken cancellationToken = default)
    {
        var deliveries = await dbContext.ResponsibilityTermDeliveries.AsNoTracking()
            .Where(d => d.ResponsibilityTermId == termId)
            .OrderByDescending(d => d.AttemptedAtUtc)
            .ToListAsync(cancellationToken);

        return deliveries.Select(ToRecord).ToList();
    }

    /// <summary>Cheap aggregate used to enrich the term detail response — never loads the full delivery history (and never exposes any recipient email) just to answer "was this ever sent, and when last, and is a send currently unresolved."</summary>
    public async Task<(DateTime? LastSentAtUtc, int SendAttemptCount, bool HasAttemptingDelivery)> GetSendSummaryAsync(
        Guid termId, CancellationToken cancellationToken = default)
    {
        var attempts = await dbContext.ResponsibilityTermDeliveries.AsNoTracking()
            .Where(d => d.ResponsibilityTermId == termId)
            .Select(d => new { d.Status, d.SentAtUtc })
            .ToListAsync(cancellationToken);

        var lastSent = attempts.Where(a => a.Status == DeliveryStatus.Sent).Select(a => a.SentAtUtc).Max();
        var hasAttempting = attempts.Any(a => a.Status == DeliveryStatus.Attempting);
        return (lastSent, attempts.Count, hasAttempting);
    }

    /// <summary>
    /// Sends (or resends) the current Responsibility Term PDF. Rejects a
    /// Superseded term outright (Phase 7 scope) — history for a
    /// superseded term's earlier sends remains readable via
    /// <see cref="ListAsync"/> regardless.
    /// </summary>
    public async Task<ServiceResult<ResponsibilityTermDeliveryRecord>> SendAsync(
        Guid termId, string? recipientOverride, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var term = await dbContext.ResponsibilityTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);
        if (term is null)
        {
            return ServiceResult.Fail<ResponsibilityTermDeliveryRecord>(ServiceErrorCode.NotFound, "Responsibility Term not found.");
        }

        if (!ResponsibilityTermDeliveryRules.CanSend(term.Status))
        {
            return ServiceResult.Fail<ResponsibilityTermDeliveryRecord>(
                ServiceErrorCode.TermNotCurrent, "Only the current version of a Responsibility Term can be newly sent for signature.");
        }

        var handover = await dbContext.EquipmentHandovers.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == term.EquipmentHandoverId, cancellationToken);
        if (handover is not null && !ResponsibilityTermRules.AllowsSignatureWorkflow(handover.Status))
        {
            // A carrier term may be prepared after dispatch, but it is only
            // sent once physical receipt is confirmed ("Confirmar entrega").
            return ServiceResult.Fail<ResponsibilityTermDeliveryRecord>(
                ServiceErrorCode.HandoverNotFinalized, "O termo só pode ser enviado depois de confirmada a entrega dos equipamentos.");
        }

        // Everything below comes from THIS version's own frozen snapshot — the
        // data its PDF was rendered from — never from the Handover.
        if (term.ConsultantSnapshot is null)
        {
            // Only a legacy version whose snapshot could not be recovered;
            // every version generated by this codebase has one. Fail safely.
            return ServiceResult.Fail<ResponsibilityTermDeliveryRecord>(
                ServiceErrorCode.SnapshotIncomplete, "This Responsibility Term version has no consultant snapshot.");
        }

        var overridden = !string.IsNullOrWhiteSpace(recipientOverride);
        var recipient = overridden ? recipientOverride!.Trim() : term.ConsultantSnapshot.Email;

        if (!ResponsibilityTermDeliveryRules.IsValidEmail(recipient))
        {
            return ServiceResult.Fail<ResponsibilityTermDeliveryRecord>(
                ServiceErrorCode.InvalidRecipient, "The recipient email address is missing or invalid.");
        }

        await using var pdfStream = await fileStorage.OpenReadAsync(term.StorageKey, cancellationToken);
        if (pdfStream is null)
        {
            return ServiceResult.Fail<ResponsibilityTermDeliveryRecord>(
                ServiceErrorCode.NotFound, "The generated Responsibility Term PDF could not be located in storage.");
        }

        await using var pdfBuffer = new MemoryStream();
        await pdfStream.CopyToAsync(pdfBuffer, cancellationToken);
        var pdfBytes = pdfBuffer.ToArray();

        // Concise identification only — e.g. "Portátil · HP 15-fd1025np ·
        // LT-0012" — never the full specification/serial (Phase 7 scope's PII-minimization discipline extends to this copy too).
        var equipmentDescriptions = await dbContext.ResponsibilityTermItems.AsNoTracking()
            .Where(i => i.ResponsibilityTermId == term.Id)
            .OrderBy(i => i.SortOrder)
            .Select(i => i.EquipmentSnapshot)
            .ToListAsync(cancellationToken);
        var equipmentLines = equipmentDescriptions
            .Select(s => $"{s.EquipmentTypeName} · {string.Join(" ", new[] { s.Manufacturer, s.Model }.Where(v => !string.IsNullOrWhiteSpace(v)))} · {s.AssetTag}")
            .ToList();

        var (subject, body) = ResponsibilityTermEmailTemplate.Build(term.DocumentNumber, term.ConsultantSnapshot.FullName, equipmentLines);

        var message = new EmailMessage(
            recipient, subject, body, new EmailAttachment(term.FileName, term.ContentType, pdfBytes), IncludeSignature: true);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var delivery = new ResponsibilityTermDelivery
        {
            Id = Guid.NewGuid(),
            ResponsibilityTermId = termId,
            RecipientEmailSnapshot = recipient,
            RecipientOverridden = overridden,
            Status = DeliveryStatus.Attempting,
            AttemptedAtUtc = now,
            SentByUserId = actorUserId,
            SentByDisplayNameSnapshot = actorDisplayName,
            CreatedAtUtc = now,
        };

        // Durable-attempt-before-side-effect invariant (ADR-052 update): if
        // this insert cannot be committed, the exception propagates
        // immediately and emailSender.SendAsync is never reached — there is
        // no code path below this line that can run without a committed
        // Attempting row already visible to any other database connection.
        dbContext.ResponsibilityTermDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (EmailSendException ex)
        {
            emailFailureLogger.DeliveryFailed(EmailMessageTypes.ResponsibilityTerm, delivery.Id, ex);

            if (!ResponsibilityTermDeliveryRules.CanTransition(delivery.Status, DeliveryStatus.Failed))
            {
                throw new InvalidOperationException($"Illegal delivery-attempt transition {delivery.Status} -> Failed.");
            }

            delivery.Status = DeliveryStatus.Failed;
            delivery.FailureCode = ToDeliveryFailureCode(ex.Reason);

            // If this update cannot be persisted (e.g. the database becomes
            // unavailable in exactly this window), the exception propagates
            // uncaught — deliberately: the row must remain Attempting, never
            // be guessed at as Failed, and no audit entry must be written for
            // an outcome that was never durably recorded (ADR-052 update). The
            // outcome and its audit row commit together (ADR-076): a failed
            // audit insert likewise leaves the row Attempting.
            await using var failedTransaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ResponsibilityTerm.SendFailed,
                EntityType: "ResponsibilityTerm",
                EntityId: termId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { term.DocumentNumber, term.Version, DeliveryAttemptId = delivery.Id, FailureCode = delivery.FailureCode.ToString() }), cancellationToken);
            await failedTransaction.CommitIfOwnedAsync(cancellationToken);

            return ServiceResult.Fail<ResponsibilityTermDeliveryRecord>(
                ServiceErrorCode.EmailSendFailed, "The Responsibility Term could not be sent. The attempt was recorded; you may retry.");
        }

        if (!ResponsibilityTermDeliveryRules.CanTransition(delivery.Status, DeliveryStatus.Sent))
        {
            throw new InvalidOperationException($"Illegal delivery-attempt transition {delivery.Status} -> Sent.");
        }

        delivery.Status = DeliveryStatus.Sent;
        delivery.SentAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Same discipline as the Failed branch above: if this update fails to
        // persist, the exception propagates uncaught and the row remains
        // Attempting — the application does not claim certainty it does not
        // have (ADR-052 update).
        await using var sentTransaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.ResponsibilityTerm.Sent,
            EntityType: "ResponsibilityTerm",
            EntityId: termId.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { term.DocumentNumber, term.Version, DeliveryAttemptId = delivery.Id, RecipientOverridden = overridden }), cancellationToken);
        await sentTransaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToRecord(delivery));
    }

    private static DeliveryFailureCode ToDeliveryFailureCode(EmailSendFailureReason reason) => reason switch
    {
        EmailSendFailureReason.NotConfigured => DeliveryFailureCode.ConfigurationError,
        EmailSendFailureReason.Rejected => DeliveryFailureCode.ProviderRejected,
        EmailSendFailureReason.Temporary => DeliveryFailureCode.TemporaryFailure,
        _ => DeliveryFailureCode.Unknown,
    };

    private static ResponsibilityTermDeliveryRecord ToRecord(ResponsibilityTermDelivery d) => new(
        d.Id, d.ResponsibilityTermId, d.RecipientEmailSnapshot, d.RecipientOverridden, d.Status,
        d.AttemptedAtUtc, d.SentAtUtc, d.FailureCode, d.SentByDisplayNameSnapshot, d.CreatedAtUtc);
}
