using ClosedXML.Excel;
using EquipmentManagement.Infrastructure.DataImports;

namespace EquipmentManagement.UnitTests.Infrastructure.DataImports;

/// <summary>
/// Mandatory formula-injection regression coverage (Phase 8 scope). Asserts the actual, verified safety property — no OOXML
/// <c>&lt;f&gt;</c> formula element and Text data type are ever produced,
/// checked directly against a real saved-and-reopened workbook — rather than
/// assuming a particular defense mechanism's implementation detail (see
/// <see cref="SafeExcelCellWriter"/>'s remarks on why a literal leading quote
/// is not itself the guarantee).
/// </summary>
public class SafeExcelCellWriterTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://evil.example\",\"click\")")]
    [InlineData("+123")]
    [InlineData("-CMD|'/C calc'!A1")]
    [InlineData("@SUM(1,1)")]
    public void WriteText_FormulaTriggeringValue_NeverBecomesAFormula(string maliciousValue)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        var cell = sheet.Cell(1, 1);
        SafeExcelCellWriter.WriteText(cell, maliciousValue);

        Assert.False(cell.HasFormula);
        Assert.Equal(XLDataType.Text, cell.DataType);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        // The real proof: inspect the raw OOXML this actually writes to
        // disk — no <f> formula element anywhere, only a plain shared-string
        // text reference. This is what makes a genuine Excel opening this
        // exact file render the value as literal text rather than evaluate it.
        stream.Position = 0;
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
        {
            var sharedStrings = archive.GetEntry("xl/sharedStrings.xml");
            Assert.NotNull(sharedStrings);
            using var reader = new StreamReader(sharedStrings.Open());
            var xml = reader.ReadToEnd();
            Assert.DoesNotContain("<x:f>", xml);
            Assert.DoesNotContain("<f>", xml);
        }

        stream.Position = 0;
        using var reopened = new XLWorkbook(stream);
        var reopenedCell = reopened.Worksheet(1).Cell(1, 1);
        Assert.False(reopenedCell.HasFormula);
        Assert.Equal(XLDataType.Text, reopenedCell.DataType);
    }

    [Fact]
    public void WriteText_OrdinaryValue_IsUnchanged()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        SafeExcelCellWriter.WriteText(sheet.Cell(1, 1), "Maria Exemplo");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        using var reopened = new XLWorkbook(stream);
        var cell = reopened.Worksheet(1).Cell(1, 1);

        Assert.False(cell.HasFormula);
        Assert.Equal("Maria Exemplo", cell.GetString());
    }

    [Fact]
    public void WriteText_NullValue_WritesEmptyString()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        SafeExcelCellWriter.WriteText(sheet.Cell(1, 1), null);

        Assert.Equal(string.Empty, sheet.Cell(1, 1).GetString());
    }
}
