using ClosedXML.Excel;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>One row of a template's "Instruções" table: what a column is, whether it must be filled in, how, and a synthetic example.</summary>
internal sealed record TemplateColumnGuide(string Header, string Required, string Guidance, string Example);

/// <summary>
/// The shared look of every generated workbook — import templates and
/// exports alike — so a sheet is readable as downloaded, without HR having
/// to resize columns first: a filled, frozen header row, explicit column
/// widths, and wrapped instruction text with row heights set to fit it.
/// Deliberately plain (one header colour pair, no borders, no merged data
/// cells) so the sheets stay easy to edit and to filter.
/// </summary>
internal static class ExcelSheetLayout
{
    public const string DateFormat = "dd/MM/yyyy";
    public const string TextFormat = "@";

    private const double HeaderRowHeight = 24;
    private const double LineHeight = 15;
    private const double MinColumnWidth = 14;
    private const double MaxColumnWidth = 60;

    private static readonly XLColor StrongHeaderFill = XLColor.FromHtml("#1F3864");
    private static readonly XLColor SoftHeaderFill = XLColor.FromHtml("#DCE6F1");
    private static readonly XLColor SoftHeaderFont = XLColor.FromHtml("#1F3864");

    /// <summary>
    /// Writes the header row and freezes it. <paramref name="emphasized"/>
    /// marks the columns whose header gets the strong fill — in a template,
    /// the fields that must always be filled in; in an export, every column.
    /// </summary>
    public static void WriteHeaderRow(IXLWorksheet sheet, IReadOnlyList<string> headers, Func<int, bool> emphasized)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.SetValue(headers[i]);
            StyleHeaderCell(cell, emphasized(i));
        }

        sheet.Row(1).Height = HeaderRowHeight;
        sheet.SheetView.FreezeRows(1);
    }

    /// <summary>Sizes each column to its content, within sensible bounds — never the unreadably narrow default, never wider than a screen.</summary>
    public static void FitColumns(IXLWorksheet sheet, int columnCount)
    {
        sheet.Columns(1, columnCount).AdjustToContents();
        for (var i = 1; i <= columnCount; i++)
        {
            var column = sheet.Column(i);

            // A header is bold and carries the filter button in an export, so
            // content-fitting alone leaves it cramped: allow for both.
            var headerWidth = sheet.Cell(1, i).GetString().Length + 5;
            column.Width = Math.Clamp(Math.Max(column.Width + 2, headerWidth), MinColumnWidth, MaxColumnWidth);
        }
    }

    /// <summary>
    /// Makes an export's header row filterable through a native Excel Table,
    /// never a worksheet AutoFilter. With a worksheet AutoFilter, filtering,
    /// clearing and saving in real Excel leaves a plain <c>_FilterDatabase</c>
    /// name next to Excel's built-in one, and every later open of the saved
    /// file stops on "Nomes em conflito" (verified in real Excel, also for a
    /// workbook Excel created itself). A Table keeps its filter inside the
    /// table and uses no such name. No theme or banding, so the header keeps
    /// its own styling. An export with no rows gets no Table (and no filter):
    /// a Table always has at least one body row, and Excel counts that empty
    /// row as a record (<c>ROWS</c>, <c>ListRows</c>, the "(Vazias)" filter).
    /// </summary>
    public static void AddFilterTable(IXLWorksheet sheet, string tableName, int columnCount, int lastRow)
    {
        if (lastRow < 2)
        {
            return;
        }

        var table = sheet.Range(1, 1, lastRow, columnCount).CreateTable(tableName);
        table.Theme = XLTableTheme.None;
        table.ShowRowStripes = false;
        table.ShowColumnStripes = false;
        table.ShowAutoFilter = true;
    }

    /// <summary>Sets explicit widths for a template's (still empty) data columns — there is no content to fit yet.</summary>
    public static void SetColumnWidths(IXLWorksheet sheet, IReadOnlyList<double> widths)
    {
        for (var i = 0; i < widths.Count; i++)
        {
            sheet.Column(i + 1).Width = widths[i];
        }
    }

    /// <summary>
    /// The same whole-column list dropdown the template has always had, with
    /// one correction: the values are written as a quoted inline list. The
    /// unquoted form is read by Excel as a reference, and Excel then refuses
    /// to open the workbook without repairing it (verified against a real
    /// Excel, ADR-070).
    /// </summary>
    public static void AddDropdown(IXLWorksheet sheet, int column, IReadOnlyList<string> values)
    {
        var validation = sheet.Column(column).CreateDataValidation();
        validation.List($"\"{string.Join(",", values)}\"");
    }

    public static void AddInstructionsSheet(
        XLWorkbook workbook, string title, IReadOnlyList<string> notes, string columnsTitle, IReadOnlyList<TemplateColumnGuide> columns)
    {
        var sheet = workbook.Worksheets.Add("Instruções");
        double[] widths = [26, 28, 70, 34];
        SetColumnWidths(sheet, widths);
        var totalWidth = widths.Sum();

        var titleCell = sheet.Cell(1, 1);
        titleCell.SetValue(title);
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 14;
        sheet.Row(1).Height = 22;

        var rowIndex = 3;
        foreach (var note in notes)
        {
            var range = sheet.Range(rowIndex, 1, rowIndex, widths.Length);
            range.Merge();
            range.FirstCell().SetValue(note);
            range.Style.Alignment.WrapText = true;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            sheet.Row(rowIndex).Height = HeightFor(note, totalWidth);
            rowIndex++;
        }

        rowIndex++;
        var columnsTitleCell = sheet.Cell(rowIndex, 1);
        columnsTitleCell.SetValue(columnsTitle);
        columnsTitleCell.Style.Font.Bold = true;
        rowIndex++;

        string[] tableHeaders = ["Coluna", "Obrigatório", "Como preencher", "Exemplo"];
        for (var i = 0; i < tableHeaders.Length; i++)
        {
            var cell = sheet.Cell(rowIndex, i + 1);
            cell.SetValue(tableHeaders[i]);
            StyleHeaderCell(cell, strong: true);
        }

        sheet.Row(rowIndex).Height = HeaderRowHeight;
        rowIndex++;

        foreach (var column in columns)
        {
            string[] values = [column.Header, column.Required, column.Guidance, column.Example];
            var height = LineHeight;
            for (var i = 0; i < values.Length; i++)
            {
                var cell = sheet.Cell(rowIndex, i + 1);
                cell.SetValue(values[i]);
                cell.Style.NumberFormat.Format = TextFormat;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                height = Math.Max(height, HeightFor(values[i], widths[i]));
            }

            sheet.Cell(rowIndex, 1).Style.Font.Bold = true;
            sheet.Row(rowIndex).Height = height;
            rowIndex++;
        }
    }

    private static void StyleHeaderCell(IXLCell cell, bool strong)
    {
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontColor = strong ? XLColor.White : SoftHeaderFont;
        cell.Style.Fill.BackgroundColor = strong ? StrongHeaderFill : SoftHeaderFill;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Alignment.WrapText = false;
    }

    /// <summary>
    /// Row height for wrapped text in a column <paramref name="width"/>
    /// characters wide. Set explicitly because a spreadsheet application only
    /// auto-fits a row it laid out itself, and never a merged one. A column
    /// width is measured in digit widths, wider than the average letter, so
    /// one line per <paramref name="width"/> characters never clips.
    /// </summary>
    private static double HeightFor(string text, double width)
    {
        var lines = Math.Max(1, (int)Math.Ceiling(text.Length / width));
        return (lines * LineHeight) + 3;
    }
}
