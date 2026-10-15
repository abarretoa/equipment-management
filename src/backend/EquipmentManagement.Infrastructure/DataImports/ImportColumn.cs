using EquipmentManagement.Domain.DataImports;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// One column of an Excel import. <see cref="Field"/> is the stable internal
/// key (it is what <see cref="ImportIssue.Field"/> carries and what the
/// import code reads by) and never changes with the language of the workbook;
/// <see cref="Header"/> is the canonical pt-PT header written to every newly
/// generated template and quoted in every user-facing message;
/// <see cref="LegacyHeaders"/> are the explicit, closed list of other headers
/// still accepted for the same column — the English headers of the templates
/// generated before the workbook was localized (ADR-070).
/// </summary>
public sealed record ImportColumn(string Field, string Header, bool IsHeaderRequired, IReadOnlyList<string> LegacyHeaders)
{
    /// <summary>Every header accepted for this column: the canonical one first, then the legacy aliases.</summary>
    public IEnumerable<string> AcceptedHeaders => LegacyHeaders.Prepend(Header);

    /// <summary>
    /// The only tolerance header matching has (ADR-070): surrounding
    /// whitespace, runs of inner whitespace (including the non-breaking space
    /// Excel sometimes pastes) and letter case are ignored. Nothing else is —
    /// accents are significant and there is no similarity matching, so a
    /// header either is one of the accepted ones or is not recognized at all.
    /// </summary>
    public static string NormalizeHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        var parts = header.Replace(' ', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToUpperInvariant();
    }
}
