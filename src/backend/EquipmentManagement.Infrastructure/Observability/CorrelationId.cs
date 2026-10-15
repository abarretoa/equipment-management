namespace EquipmentManagement.Infrastructure.Observability;

/// <summary>
/// Shared correlation-id conventions. Lives in Infrastructure (not Api) so both
/// <c>Api.Middleware.CorrelationIdMiddleware</c> (which sets it) and
/// <c>Infrastructure.Auditing.AuditService</c> (which reads it, and cannot depend
/// on the Api project) agree on the same header name,
/// <see cref="HttpContext.Items"/> key and acceptance policy.
/// </summary>
/// <remarks>
/// An incoming X-Correlation-ID is untrusted caller input that ends up in the
/// logging scope, the response header and <c>AuditLog.CorrelationId</c> (a
/// 100-character column). Only a short, conservative identifier is ever
/// accepted (ADR-076); anything else is silently replaced by a server-generated
/// id rather than rejected, so a malformed header can never fail a request.
/// </remarks>
public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-ID";
    public const string HttpContextItemKey = "CorrelationId";

    /// <summary>
    /// Deliberately below the 100-character <c>AuditLog.CorrelationId</c> column
    /// (enforced by a test); room for a GUID (36) or a W3C traceparent (55).
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>True for 1..<see cref="MaxLength"/> characters, each an ASCII letter, digit, '-', '_', '.' or ':'.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The caller's id when it is <see cref="IsValid"/>; a new server-generated one otherwise.</summary>
    public static string Resolve(string? incoming) => IsValid(incoming) ? incoming! : Generate();

    /// <summary>A GUID in "D" format: 36 characters, always <see cref="IsValid"/>.</summary>
    public static string Generate() => Guid.NewGuid().ToString("D");
}
