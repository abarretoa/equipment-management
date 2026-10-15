using EquipmentManagement.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EquipmentManagement.Infrastructure.Email;

/// <summary>
/// One operational log line per failed email send, so a mail outage is visible
/// in the application log and not only in the database and the UI. Logs
/// identifiers and a failure category only.
/// </summary>
/// <remarks>
/// Deliberately never logs the recipient, any person's name, the subject or
/// body, the attachment, or SMTP settings. The provider's own exception is not
/// logged either: its message can quote the recipient address or the server
/// name, so only its type name is kept.
/// </remarks>
public sealed partial class EmailFailureLogger(ILogger<EmailFailureLogger> logger, IHttpContextAccessor httpContextAccessor)
{
    /// <param name="messageType">Which of the application's emails failed (see <see cref="EmailMessageTypes"/>).</param>
    /// <param name="deliveryId">The delivery attempt row, or for a message with no attempt row, the record that triggered it.</param>
    /// <param name="exception">The classified failure; only its reason and the inner exception's type name are logged.</param>
    public void DeliveryFailed(string messageType, Guid deliveryId, EmailSendException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var correlationId = httpContextAccessor.HttpContext?.Items[CorrelationId.HttpContextItemKey] as string;

        LogDeliveryFailed(
            logger, messageType, deliveryId, exception.Reason,
            exception.InnerException?.GetType().Name ?? "None",
            CorrelationId.IsValid(correlationId) ? correlationId! : "None");
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Email delivery failed. MessageType={MessageType} DeliveryId={DeliveryId} FailureCategory={FailureCategory} ErrorType={ErrorType} CorrelationId={CorrelationId}")]
    private static partial void LogDeliveryFailed(
        ILogger logger, string messageType, Guid deliveryId, EmailSendFailureReason failureCategory, string errorType, string correlationId);
}

/// <summary>Stable names for the <c>MessageType</c> field of the failed-delivery log line.</summary>
public static class EmailMessageTypes
{
    public const string ResponsibilityTerm = "ResponsibilityTerm";
    public const string DispatchNotification = "DispatchNotification";
    public const string CollectionNotification = "CollectionNotification";
    public const string RetirementRequestNotification = "RetirementRequestNotification";
}
