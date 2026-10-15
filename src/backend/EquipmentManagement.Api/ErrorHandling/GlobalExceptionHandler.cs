using EquipmentManagement.Infrastructure.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EquipmentManagement.Api.ErrorHandling;

/// <summary>
/// Last-resort handler for unhandled exceptions. Logs full details server-side but
/// returns only a generic ProblemDetails body to the client — never a stack trace
/// or exception message, which could leak internals or (later) personal data
/// embedded in an error message. Per docs/PROJECT_SPEC.md section 22/27.
/// </summary>
public sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items.TryGetValue(CorrelationId.HttpContextItemKey, out var id)
            ? id?.ToString()
            : httpContext.TraceIdentifier;

        LogUnhandledException(logger, exception, httpContext.Request.Method, httpContext.Request.Path, correlationId);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        };
        problemDetails.Extensions["correlationId"] = correlationId;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unhandled exception processing {Method} {Path} (CorrelationId: {CorrelationId})")]
    private static partial void LogUnhandledException(
        ILogger logger, Exception exception, string method, string? path, string? correlationId);
}
