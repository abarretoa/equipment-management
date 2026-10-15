using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EquipmentManagement.UnitTests.Infrastructure.Email;

/// <summary>
/// The failed-delivery log line carries the identifiers an operator needs and
/// nothing that could identify a person or leak a credential.
/// </summary>
public sealed class EmailFailureLoggerTests
{
    private const string CorrelationIdValue = "0b9c2f0e-6c1d-4a67-9a52-3c1d0f8f6a11";

    [Fact]
    public void DeliveryFailed_LogsOneWarning_WithTypeIdCategoryAndCorrelationId()
    {
        var logger = new CapturingLogger();
        var deliveryId = Guid.NewGuid();

        CreateSut(logger, CorrelationIdValue).DeliveryFailed(
            EmailMessageTypes.ResponsibilityTerm, deliveryId,
            new EmailSendException(EmailSendFailureReason.Temporary, "The mail provider could not be reached.", new IOException("inner")));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("ResponsibilityTerm", entry.State["MessageType"]);
        Assert.Equal(deliveryId, entry.State["DeliveryId"]);
        Assert.Equal(EmailSendFailureReason.Temporary, entry.State["FailureCategory"]);
        Assert.Equal("IOException", entry.State["ErrorType"]);
        Assert.Equal(CorrelationIdValue, entry.State["CorrelationId"]);
        Assert.Contains($"DeliveryId={deliveryId}", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliveryFailed_NeverLogsTheProviderExceptionOrItsMessage()
    {
        var logger = new CapturingLogger();
        var providerException = new InvalidOperationException(
            "550 5.1.1 <maria.consultora@cliente.example.test>: Recipient address rejected; AUTH user=mailer pass=smtp-secret-value host=smtp.example.test");

        CreateSut(logger, CorrelationIdValue).DeliveryFailed(
            EmailMessageTypes.DispatchNotification, Guid.NewGuid(),
            new EmailSendException(EmailSendFailureReason.Rejected, "The mail provider rejected the send request.", providerException));

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        var everything = entry.Message + string.Join("|", entry.State.Select(p => $"{p.Key}={p.Value}"));
        Assert.DoesNotContain("maria.consultora", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cliente.example.test", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("smtp-secret-value", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("mailer", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("smtp.example.test", everything, StringComparison.Ordinal);
        Assert.Equal(
            ["CorrelationId", "DeliveryId", "ErrorType", "FailureCategory", "MessageType", "{OriginalFormat}"],
            entry.State.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DeliveryFailed_OutsideARequest_OrWithAnUnvalidatedId_LogsNoCorrelationId()
    {
        var logger = new CapturingLogger();
        var exception = new EmailSendException(EmailSendFailureReason.NotConfigured, "Email delivery is not configured.");

        new EmailFailureLogger(logger, new HttpContextAccessor()).DeliveryFailed(EmailMessageTypes.CollectionNotification, Guid.NewGuid(), exception);
        CreateSut(logger, "not a valid id\r\ninjected").DeliveryFailed(EmailMessageTypes.CollectionNotification, Guid.NewGuid(), exception);

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry =>
        {
            Assert.Equal("None", entry.State["CorrelationId"]);
            Assert.Equal("None", entry.State["ErrorType"]);
            Assert.DoesNotContain("injected", entry.Message, StringComparison.Ordinal);
        });
    }

    private static EmailFailureLogger CreateSut(CapturingLogger logger, string correlationId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[CorrelationId.HttpContextItemKey] = correlationId;
        return new EmailFailureLogger(logger, new HttpContextAccessor { HttpContext = httpContext });
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> State);

    private sealed class CapturingLogger : ILogger<EmailFailureLogger>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value)
                : [];
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception, values));
        }
    }
}
