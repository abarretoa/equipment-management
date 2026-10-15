namespace EquipmentManagement.Infrastructure.Email;

/// <summary>
/// A safe, closed classification of why a send attempt failed — never wraps
/// or exposes the raw provider/SMTP exception message, which could contain
/// secrets or connection details (Phase 7 scope). Callers map
/// this to <c>Domain.ResponsibilityTerms.DeliveryFailureCode</c> when
/// persisting a failed delivery attempt.
/// </summary>
public enum EmailSendFailureReason
{
    /// <summary>The mail provider is not configured (e.g. no SMTP host set) — an operator/deployment problem, not a transient one.</summary>
    NotConfigured,

    /// <summary>The provider rejected the message outright (e.g. invalid recipient, authentication failure).</summary>
    Rejected,

    /// <summary>A network/timeout/provider-availability failure that may succeed on retry.</summary>
    Temporary,

    Unknown,
}

/// <summary>Thrown by <see cref="IEmailSender.SendAsync"/> for any expected send failure. <see cref="Exception.Message"/> is always a safe, generic description — the real provider exception, if any, is only in <see cref="Exception.InnerException"/> for logging, never surfaced to a caller or stored in the database.</summary>
public sealed class EmailSendException(EmailSendFailureReason reason, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public EmailSendFailureReason Reason { get; } = reason;
}
