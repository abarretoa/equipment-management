using System.Globalization;
using ClosedXML.Excel;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Infrastructure.Common;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// The shared, small foundation for reading an untrusted uploaded .xlsx
/// workbook (Phase 8 scope) — structural/security checks and
/// primitive cell extraction only. Each import service (Consultant/Equipment)
/// owns its own column-name-to-canonical-field mapping and row-shredding loop
/// on top of this; this class deliberately does not become a generic
/// reflection-based mapper (Phase 8 scope).
///
/// Never evaluates formulas (ClosedXML/OpenXML never execute macros or
/// resolve external references at all — Phase 8 scope) — a
/// formula cell is always detected via <see cref="IsFormula"/> and rejected
/// by the caller, never read for its cached value.
///
/// Every failure message here reaches the person who uploaded the file, so
/// it is written in pt-PT (ADR-070).
/// </summary>
public sealed class ExcelWorkbookReader(IOptions<ImportLimitsOptions> limitsOptions)
{
    public ImportLimitsOptions Limits => limitsOptions.Value;

    /// <summary>
    /// Opens and structurally validates the workbook. Only <c>.xlsx</c> is
    /// ever attempted to be parsed — the caller must reject any other
    /// extension/content-type before calling this (Phase 8 scope: macro-enabled <c>.xlsm</c> is never accepted).
    /// </summary>
    public ServiceResult<(XLWorkbook Workbook, IXLWorksheet Worksheet)> OpenAndValidate(byte[] bytes, string fileName)
    {
        // Extension check first, and independent of content sniffing — a
        // macro-enabled .xlsm is a structurally valid OOXML zip too (same
        // container format) and could otherwise slip past the parser
        // succeeding to open it (Phase 8 scope).
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult.Fail<(XLWorkbook, IXLWorksheet)>(
                ServiceErrorCode.UnsupportedWorkbook, "Apenas são aceites ficheiros .xlsx.");
        }

        if (bytes.LongLength > Limits.MaxUploadBytes)
        {
            return ServiceResult.Fail<(XLWorkbook, IXLWorksheet)>(
                ServiceErrorCode.WorkbookTooLarge, $"O ficheiro excede o tamanho máximo permitido ({FormatMegabytes(Limits.MaxUploadBytes)} MB).");
        }

        XLWorkbook workbook;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            workbook = new XLWorkbook(stream);
        }
        catch (Exception)
        {
            // Untrusted input: any failure opening the ZIP/OOXML container
            // (corrupt, not actually an xlsx, encrypted, ...) is reported the
            // same way — never a raw parser exception (Phase 8 scope).
            return ServiceResult.Fail<(XLWorkbook, IXLWorksheet)>(
                ServiceErrorCode.UnsupportedWorkbook, "O ficheiro carregado não é um ficheiro .xlsx válido.");
        }

        if (workbook.Worksheets.Count == 0)
        {
            workbook.Dispose();
            return ServiceResult.Fail<(XLWorkbook, IXLWorksheet)>(ServiceErrorCode.MissingWorksheet, "O ficheiro não contém nenhuma folha.");
        }

        if (workbook.Worksheets.Count > Limits.MaxWorksheets)
        {
            workbook.Dispose();
            return ServiceResult.Fail<(XLWorkbook, IXLWorksheet)>(
                ServiceErrorCode.InvalidWorkbookStructure, $"O ficheiro tem demasiadas folhas (máximo: {Limits.MaxWorksheets}).");
        }

        // Only the first worksheet is ever read — a deliberate, predictable
        // strategy rather than matching by (possibly HR-renamed) sheet name.
        var worksheet = workbook.Worksheets.First();
        var usedRange = worksheet.RangeUsed();
        if (usedRange is not null)
        {
            var columnCount = usedRange.ColumnCount();
            if (columnCount > Limits.MaxColumns)
            {
                workbook.Dispose();
                return ServiceResult.Fail<(XLWorkbook, IXLWorksheet)>(
                    ServiceErrorCode.InvalidWorkbookStructure, $"A folha tem demasiadas colunas (máximo: {Limits.MaxColumns}).");
            }

            // Header row + data rows; RowsUsed() below already ignores
            // trailing formatting-only rows (Phase 8 scope).
            var rowCount = worksheet.RowsUsed().Count() - 1;
            if (rowCount > Limits.MaxRowsPerImport)
            {
                workbook.Dispose();
                return ServiceResult.Fail<(XLWorkbook, IXLWorksheet)>(
                    ServiceErrorCode.InvalidWorkbookStructure, $"A folha tem demasiadas linhas de dados (máximo: {Limits.MaxRowsPerImport}).");
            }
        }

        return ServiceResult.Ok((workbook, worksheet));
    }

    /// <summary>
    /// Maps the worksheet's header row (row 1) to the import's columns,
    /// keyed by <see cref="ImportColumn.Field"/>. A header cell matches a
    /// column when, after <see cref="ImportColumn.NormalizeHeader"/>
    /// (trimmed, inner whitespace collapsed, case-insensitive — Phase 8 scope), it equals that column's pt-PT header or one of its
    /// explicit legacy aliases (ADR-070) — never by similarity. Rejects a
    /// missing required column, and a column supplied twice (whether under
    /// the same header or under its pt-PT and legacy names at once). Unknown
    /// extra columns are silently ignored — not read, not warned about
    /// (the smallest predictable strategy).
    /// </summary>
    public static ServiceResult<IReadOnlyDictionary<string, int>> MapHeaders(IXLWorksheet worksheet, IReadOnlyList<ImportColumn> columns)
    {
        var columnByHeader = new Dictionary<string, ImportColumn>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            foreach (var accepted in column.AcceptedHeaders)
            {
                columnByHeader[ImportColumn.NormalizeHeader(accepted)] = column;
            }
        }

        var headerRow = worksheet.Row(1);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var duplicates = new List<string>();

        var lastColumn = worksheet.RangeUsed()?.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var col = 1; col <= lastColumn; col++)
        {
            var header = ImportColumn.NormalizeHeader(headerRow.Cell(col).GetString());
            if (header.Length == 0 || !columnByHeader.TryGetValue(header, out var column))
            {
                continue;
            }

            if (!map.TryAdd(column.Field, col))
            {
                duplicates.Add(column.Header);
            }
        }

        if (duplicates.Count > 0)
        {
            return ServiceResult.Fail<IReadOnlyDictionary<string, int>>(
                ServiceErrorCode.MissingRequiredColumn,
                $"O ficheiro tem colunas em duplicado: {string.Join(", ", duplicates.Distinct())}. Cada coluna só pode aparecer uma vez.");
        }

        var missing = columns.Where(c => c.IsHeaderRequired && !map.ContainsKey(c.Field)).Select(c => c.Header).ToList();
        if (missing.Count > 0)
        {
            return ServiceResult.Fail<IReadOnlyDictionary<string, int>>(
                ServiceErrorCode.MissingRequiredColumn,
                $"Faltam colunas obrigatórias no ficheiro: {string.Join(", ", missing)}. Utilize o modelo de importação sem alterar o nome das colunas.");
        }

        return ServiceResult.Ok<IReadOnlyDictionary<string, int>>(map);
    }

    private static string FormatMegabytes(long bytes) =>
        (bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.GetCultureInfo("pt-PT"));

    /// <summary>Data rows (after the header), in worksheet order — the actual row number is preserved via <see cref="IXLRow.RowNumber"/> for diagnostics (Phase 8 scope).</summary>
    public static IEnumerable<IXLRow> GetDataRows(IXLWorksheet worksheet) =>
        worksheet.RowsUsed().Where(r => r.RowNumber() > 1);

    public static bool IsFormula(IXLRow row, int? columnIndex) => columnIndex is { } c && row.Cell(c).HasFormula;

    /// <summary>Plain text field — trimmed, truncated-detection via <paramref name="tooLong"/> rather than silently truncating (Phase 8 scope).</summary>
    public string? ReadText(IXLRow row, int? columnIndex, out bool tooLong)
    {
        tooLong = false;
        if (columnIndex is not { } c)
        {
            return null;
        }

        var text = row.Cell(c).GetString().Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length > Limits.MaxCellLength)
        {
            tooLong = true;
        }

        return text;
    }

    /// <summary>
    /// A textual identifier (NIF/SerialNumber) — Excel silently coerces long
    /// digit strings to <c>Number</c> cells, which can lose leading zeros or
    /// switch to scientific notation. Rather than trust that lossy
    /// stringification, a Number-typed cell in an identifier column is always
    /// flagged ambiguous and rejected (Phase 8 scope) —
    /// never guessed at.
    /// </summary>
    public string? ReadIdentifierText(IXLRow row, int? columnIndex, out bool ambiguousNumeric, out bool tooLong)
    {
        ambiguousNumeric = false;
        tooLong = false;
        if (columnIndex is not { } c)
        {
            return null;
        }

        var cell = row.Cell(c);
        if (cell.DataType == XLDataType.Number)
        {
            ambiguousNumeric = true;
            return null;
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length > Limits.MaxCellLength)
        {
            tooLong = true;
        }

        return text;
    }

    /// <summary>
    /// A genuine Excel date cell is read directly; a text cell is accepted
    /// only in the exact <c>dd/MM/yyyy</c> form, parsed with the invariant
    /// culture — never the server's locale (Phase 8 scope).
    /// Anything else (a different format, an ambiguous short form like
    /// "03/04/26") is reported <paramref name="invalid"/> rather than guessed.
    /// </summary>
    public static DateOnly? ReadDate(IXLRow row, int? columnIndex, out bool invalid)
    {
        invalid = false;
        if (columnIndex is not { } c)
        {
            return null;
        }

        var cell = row.Cell(c);
        if (cell.DataType == XLDataType.DateTime)
        {
            return DateOnly.FromDateTime(cell.GetDateTime());
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        invalid = true;
        return null;
    }
}
