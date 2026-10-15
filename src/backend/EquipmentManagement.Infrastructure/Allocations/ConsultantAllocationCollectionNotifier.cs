using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Consultants;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Persistence;

namespace EquipmentManagement.Infrastructure.Allocations;

/// <summary>
/// Sends the equipment-collection notification after
/// <see cref="ConsultantAllocationService.EndAsync"/> ends an allocation as
/// end-of-engagement with the Consultant still holding active equipment.
/// Mirrors <c>ResponsibilityTermDeliveryService</c>'s ADR-052 ordering
/// discipline exactly: a durable <see cref="DeliveryStatus.Attempting"/> row
/// is inserted and committed before the email reaches
/// <see cref="IEmailSender"/>. The allocation ending is already durably
/// committed by the caller before this runs — email failure here never rolls
/// back the ending or touches equipment custody/lifecycle.
/// </summary>
public sealed class ConsultantAllocationCollectionNotifier(
    ApplicationDbContext dbContext, AuditService auditService, IEmailSender emailSender, EmailFailureLogger emailFailureLogger,
    TimeProvider timeProvider)
{
    public async Task SendAsync(
        ConsultantAllocation allocation, Consultant consultant, IReadOnlyList<(Equipment Equipment, EquipmentType Type)> assignedEquipment,
        Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken)
    {
        if (!ResponsibilityTermDeliveryRules.IsValidEmail(consultant.Email))
        {
            return;
        }

        var lines = assignedEquipment
            .Select(x => new DispatchEquipmentLine(x.Equipment.AssetTag, x.Type.Name, x.Equipment.Manufacturer, x.Equipment.Model, null, null))
            .ToList();

        var (subject, body) = ConsultantAllocationCollectionEmailTemplate.Build(
            consultant.FullName, lines, allocation.ExpectedCollectionFromDate!.Value, allocation.ExpectedCollectionToDate);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var notification = new ConsultantAllocationCollectionNotification
        {
            Id = Guid.NewGuid(),
            ConsultantAllocationId = allocation.Id,
            RecipientEmailSnapshot = consultant.Email,
            Status = DeliveryStatus.Attempting,
            AttemptedAtUtc = now,
            CreatedAtUtc = now,
        };

        dbContext.ConsultantAllocationCollectionNotifications.Add(notification);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAsync(new EmailMessage(consultant.Email, subject, body, IncludeSignature: true), cancellationToken);
        }
        catch (EmailSendException ex)
        {
            emailFailureLogger.DeliveryFailed(EmailMessageTypes.CollectionNotification, notification.Id, ex);

            notification.Status = DeliveryStatus.Failed;
            notification.FailureCode = ToDeliveryFailureCode(ex.Reason);

            // The send already happened (outside any transaction); only the
            // recorded outcome and its audit row commit together.
            await using var failedTransaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ConsultantAllocation.CollectionNotificationFailed,
                EntityType: "ConsultantAllocation",
                EntityId: allocation.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { NotificationId = notification.Id, FailureCode = notification.FailureCode.ToString() }), cancellationToken);
            await failedTransaction.CommitIfOwnedAsync(cancellationToken);
            return;
        }

        notification.Status = DeliveryStatus.Sent;
        notification.SentAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var sentTransaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.ConsultantAllocation.CollectionNotificationSent,
            EntityType: "ConsultantAllocation",
            EntityId: allocation.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { NotificationId = notification.Id }), cancellationToken);
        await sentTransaction.CommitIfOwnedAsync(cancellationToken);
    }

    private static DeliveryFailureCode ToDeliveryFailureCode(EmailSendFailureReason reason) => reason switch
    {
        EmailSendFailureReason.NotConfigured => DeliveryFailureCode.ConfigurationError,
        EmailSendFailureReason.Rejected => DeliveryFailureCode.ProviderRejected,
        EmailSendFailureReason.Temporary => DeliveryFailureCode.TemporaryFailure,
        _ => DeliveryFailureCode.Unknown,
    };
}
