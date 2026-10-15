using System.Globalization;

namespace EquipmentManagement.Infrastructure.Documents;

/// <summary>
/// The single, explicit culture used for every date/number displayed inside a
/// generated document (Phase 6 scope) — never
/// <see cref="CultureInfo.CurrentCulture"/> or an implicit dependency on the
/// server machine's locale. The product targets Portugal and
/// <c>docs/PROJECT_SPEC.md</c> does not specify a document language, so
/// Portuguese (pt-PT) is the deliberate default for the initial template — centralized here so a future
/// multi-language template only has to change one place, not every call site
/// that formats a date. Also reused by backend-rendered email templates
/// (e.g. <c>EquipmentRetirementRequestEmailTemplate</c>) for the same reason:
/// one shared, explicit-UTC-label convention for any server-side surface that
/// shows a UTC instant to a user, rather than each template inventing its own.
/// </summary>
public static class DocumentCulture
{
    public static readonly CultureInfo PtPt = CultureInfo.GetCultureInfo("pt-PT");

    /// <summary>European day/month/year, e.g. "10/09/2026" — never the ambiguous US month/day ordering.</summary>
    public static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", PtPt);

    public static string FormatDateTimeUtc(DateTime utc) => $"{utc.ToString("dd/MM/yyyy HH:mm", PtPt)} UTC";
}
