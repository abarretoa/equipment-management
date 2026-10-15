using EquipmentManagement.Infrastructure.Observability;

namespace EquipmentManagement.Api.Middleware;

/// <summary>
/// Ensures every request carries a correlation ID: reuses an incoming
/// X-Correlation-ID header (useful when a caller already has one, e.g. a future
/// gateway) only when it passes <see cref="CorrelationId.IsValid"/>, and
/// generates one otherwise — an oversized or malformed header is replaced, never
/// propagated and never an error (ADR-076). The ID is echoed on the response, exposed
/// to the rest of the pipeline via HttpContext.Items, and pushed into the logging
/// scope so every log line for this request can be filtered by it — this is also
/// what every audit record carries as CorrelationId (docs/PROJECT_SPEC.md section
/// 15), via Infrastructure.Auditing.AuditService reading the same
/// HttpContext.Items key (see CorrelationId.HttpContextItemKey).
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // A repeated header arrives as several values; ToString() joins them with
        // ',', which IsValid rejects, so that case is replaced too.
        var correlationId = CorrelationId.Resolve(
            context.Request.Headers.TryGetValue(CorrelationId.HeaderName, out var incoming) ? incoming.ToString() : null);

        context.Items[CorrelationId.HttpContextItemKey] = correlationId;
        context.Response.Headers[CorrelationId.HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}
