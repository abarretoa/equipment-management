using System.Threading;

namespace EquipmentManagement.Infrastructure.Email;

/// <summary>
/// In-memory fake used in the "Testing" environment (see
/// <c>Infrastructure.DependencyInjection</c>) and by unit tests — no
/// automated test ever sends a real email (Phase 7 scope). Every
/// accepted message is retained so a test can assert on recipient, subject,
/// body and — critically — that the attachment bytes are byte-identical to
/// the stored Responsibility Term PDF (Phase 7 scope). Registered
/// as a singleton so the same instance is observed by the service under test
/// and by the test's own assertions.
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sentMessages = [];
    private readonly Lock _gate = new();

    /// <summary>Set by a test to make the next <see cref="SendAsync"/> call fail, to exercise the failed-send path without a real provider.</summary>
    public EmailSendFailureReason? FailNextSendWith { get; set; }

    public IReadOnlyList<EmailMessage> SentMessages
    {
        get
        {
            lock (_gate)
            {
                return _sentMessages.ToList();
            }
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (FailNextSendWith is { } reason)
        {
            FailNextSendWith = null;
            throw new EmailSendException(reason, "Simulated failure requested by test.");
        }

        lock (_gate)
        {
            _sentMessages.Add(message);
        }

        return Task.CompletedTask;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _sentMessages.Clear();
        }

        FailNextSendWith = null;
    }
}
