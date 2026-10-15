using ClosedXML.Excel;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Writes user-controlled text into a spreadsheet cell as a literal text
/// value, never a formula — the mandatory defense against spreadsheet-formula
/// injection on export (Phase 8 scope). Consultant names,
/// company names, role/function, manufacturer/model, and notes all pass
/// through this before being written to any generated workbook.
///
/// The actual, verified safety property (confirmed by inspecting the raw
/// OOXML this produces, not assumed): <c>IXLCell.SetValue(string)</c> never
/// creates a <c>&lt;f&gt;</c> formula element, regardless of what the string
/// starts with — only the distinct <c>SetFormulaA1</c>/<c>FormulaA1</c> API
/// does that, which nothing in this codebase's export path ever calls. Every
/// cell this method writes is therefore, unconditionally, a plain OOXML text
/// cell (shared string), which is why a genuine Excel opening the file we
/// generate renders it as literal text rather than evaluating it — this is a
/// materially different, stronger guarantee than the classic CSV-injection
/// risk, where a format-free CSV field's type is guessed purely from its
/// leading character.
///
/// A leading <c>=</c>/<c>+</c>/<c>-</c>/<c>@</c> is additionally prefixed
/// with a single quote — the same "force text" convention Excel itself uses
/// for manually-typed cell entry — as defense-in-depth for a tool other than
/// this codebase's own ClosedXML round-trip. (ClosedXML's own writer
/// recognizes and strips a leading quote the same way genuine Excel does, so
/// it does not appear verbatim in the persisted string; this is expected and
/// does not weaken the core guarantee above, which does not depend on it.)
/// </summary>
public static class SafeExcelCellWriter
{
    private static readonly char[] FormulaTriggerChars = ['=', '+', '-', '@'];

    /// <summary>Writes <paramref name="value"/> as a literal text cell, with an explicit Text number format so no downstream re-interpretation as a number/date/formula occurs.</summary>
    public static void WriteText(IXLCell cell, string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && FormulaTriggerChars.Contains(text[0]))
        {
            text = "'" + text;
        }

        cell.SetValue(text);
        cell.Style.NumberFormat.Format = "@";
    }
}
