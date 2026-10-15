using ClosedXML.Excel;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.DataImports;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.UnitTests.Infrastructure.DataImports;

public class ExcelWorkbookReaderTests
{
    private static ExcelWorkbookReader CreateReader(ImportLimitsOptions? limits = null) =>
        new(Options.Create(limits ?? new ImportLimitsOptions()));

    private static readonly ImportColumn FullName = new("FullName", "Nome completo", IsHeaderRequired: true, ["FullName"]);
    private static readonly ImportColumn Nif = new("NIF", "NIF", IsHeaderRequired: true, []);
    private static readonly ImportColumn Serial = new("SerialNumber", "Número de série", IsHeaderRequired: true, ["SerialNumber"]);
    private static readonly ImportColumn Role = new("RoleOrFunction", "Função", IsHeaderRequired: false, ["RoleOrFunction"]);

    private static ServiceResult<IReadOnlyDictionary<string, int>> MapHeaders(IReadOnlyList<ImportColumn> columns, params string[] headerRow)
    {
        var bytes = BuildWorkbook(sheet =>
        {
            for (var i = 0; i < headerRow.Length; i++)
            {
                sheet.Cell(1, i + 1).SetValue(headerRow[i]);
            }
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;
        return ExcelWorkbookReader.MapHeaders(opened.Value.Worksheet, columns);
    }

    private static byte[] BuildWorkbook(Action<IXLWorksheet> configure)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        configure(sheet);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public void OpenAndValidate_WellFormedWorkbook_Succeeds()
    {
        var bytes = BuildWorkbook(sheet => sheet.Cell(1, 1).SetValue("FullName"));
        var result = CreateReader().OpenAndValidate(bytes, "consultants.xlsx");

        Assert.True(result.Succeeded);
        result.Value.Workbook.Dispose();
    }

    [Fact]
    public void OpenAndValidate_RejectsNonXlsxExtension()
    {
        var bytes = BuildWorkbook(sheet => sheet.Cell(1, 1).SetValue("FullName"));
        var result = CreateReader().OpenAndValidate(bytes, "consultants.xlsm");

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.UnsupportedWorkbook, result.ErrorCode);
    }

    [Fact]
    public void OpenAndValidate_MalformedBytes_ReturnsUnsupportedWorkbook()
    {
        var bytes = "this is not a real xlsx file at all"u8.ToArray();
        var result = CreateReader().OpenAndValidate(bytes, "consultants.xlsx");

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.UnsupportedWorkbook, result.ErrorCode);
    }

    [Fact]
    public void OpenAndValidate_ExceedsMaxUploadBytes_ReturnsWorkbookTooLarge()
    {
        var bytes = BuildWorkbook(sheet => sheet.Cell(1, 1).SetValue("FullName"));
        var reader = CreateReader(new ImportLimitsOptions { MaxUploadBytes = 10 });
        var result = reader.OpenAndValidate(bytes, "consultants.xlsx");

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.WorkbookTooLarge, result.ErrorCode);
    }

    [Fact]
    public void OpenAndValidate_TooManyDataRows_ReturnsInvalidWorkbookStructure()
    {
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue("FullName");
            for (var i = 0; i < 10; i++)
            {
                sheet.Cell(i + 2, 1).SetValue($"Row {i}");
            }
        });
        var reader = CreateReader(new ImportLimitsOptions { MaxRowsPerImport = 5 });
        var result = reader.OpenAndValidate(bytes, "consultants.xlsx");

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.InvalidWorkbookStructure, result.ErrorCode);
    }

    [Fact]
    public void MapHeaders_AllRequiredPresent_Succeeds()
    {
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue(" FullName ");
            sheet.Cell(1, 2).SetValue("nif");
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;

        var result = ExcelWorkbookReader.MapHeaders(opened.Value.Worksheet, [FullName, Nif]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!["FullName"]);
        Assert.Equal(2, result.Value!["NIF"]);
    }

    [Fact]
    public void MapHeaders_MissingRequiredHeader_Fails()
    {
        var bytes = BuildWorkbook(sheet => sheet.Cell(1, 1).SetValue("FullName"));
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;

        var result = ExcelWorkbookReader.MapHeaders(opened.Value.Worksheet, [FullName, Nif]);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.MissingRequiredColumn, result.ErrorCode);
    }

    [Fact]
    public void MapHeaders_DuplicateHeader_Fails()
    {
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue("FullName");
            sheet.Cell(1, 2).SetValue("FullName");
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;

        var result = ExcelWorkbookReader.MapHeaders(opened.Value.Worksheet, [FullName]);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.MissingRequiredColumn, result.ErrorCode);
    }

    [Fact]
    public void MapHeaders_CanonicalPortugueseHeaders_MapToTheInternalFields()
    {
        var result = MapHeaders([FullName, Nif, Serial, Role], "Nome completo", "NIF", "Número de série", "Função");

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!["FullName"]);
        Assert.Equal(2, result.Value["NIF"]);
        Assert.Equal(3, result.Value["SerialNumber"]);
        Assert.Equal(4, result.Value["RoleOrFunction"]);
    }

    [Fact]
    public void MapHeaders_LegacyEnglishHeaders_MapToTheSameInternalFields()
    {
        var result = MapHeaders([FullName, Nif, Serial, Role], "FullName", "NIF", "SerialNumber", "RoleOrFunction");

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!["FullName"]);
        Assert.Equal(3, result.Value["SerialNumber"]);
        Assert.Equal(4, result.Value["RoleOrFunction"]);
    }

    [Fact]
    public void MapHeaders_MixedPortugueseAndLegacyHeaders_InAnyOrder_AreAccepted()
    {
        var result = MapHeaders([FullName, Nif, Serial], "SerialNumber", "Nome completo", "NIF");

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!["FullName"]);
        Assert.Equal(1, result.Value["SerialNumber"]);
    }

    [Theory]
    [InlineData("  Número de série  ")]
    [InlineData("NÚMERO DE SÉRIE")]
    [InlineData("número de série")]
    [InlineData("Número   de  série")]
    [InlineData("Número\u00A0de\u00A0série")]
    [InlineData(" serialnumber ")]
    public void MapHeaders_IgnoresSurroundingAndRepeatedWhitespaceAndCase(string header)
    {
        var result = MapHeaders([Serial], header);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!["SerialNumber"]);
    }

    [Theory]
    [InlineData("Numero de serie")] // accents are significant
    [InlineData("Número de séries")]
    [InlineData("N.º de série")]
    [InlineData("Serial")]
    [InlineData("Serial Number")] // the legacy header had no space
    [InlineData("Númerodesérie")]
    public void MapHeaders_NeverMatchesASimilarLookingHeader(string header)
    {
        var result = MapHeaders([Serial], header);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.MissingRequiredColumn, result.ErrorCode);
    }

    [Fact]
    public void MapHeaders_MissingRequiredColumn_IsNamedByItsPortugueseHeader()
    {
        var result = MapHeaders([FullName, Nif, Serial], "Nome completo");

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.MissingRequiredColumn, result.ErrorCode);
        Assert.Contains("Faltam colunas obrigatórias", result.ErrorMessage);
        Assert.Contains("NIF, Número de série", result.ErrorMessage);
        Assert.DoesNotContain("SerialNumber", result.ErrorMessage);
    }

    [Fact]
    public void MapHeaders_MissingOptionalColumn_IsNotAnError()
    {
        var result = MapHeaders([FullName, Role], "Nome completo");

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.ContainsKey("RoleOrFunction"));
    }

    [Fact]
    public void MapHeaders_SameColumnUnderItsPortugueseAndLegacyHeader_IsADuplicate()
    {
        var result = MapHeaders([FullName, Serial], "Nome completo", "Número de série", "SerialNumber");

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorCode.MissingRequiredColumn, result.ErrorCode);
        Assert.Contains("colunas em duplicado: Número de série", result.ErrorMessage);
    }

    [Fact]
    public void MapHeaders_UnknownExtraColumns_AreIgnored()
    {
        var result = MapHeaders([FullName], "ID do consultor", "Nome completo", "Qualquer coisa");

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!["FullName"]);
        Assert.Single(result.Value);
    }

    [Fact]
    public void OpenAndValidate_Failures_AreReportedInPortuguese()
    {
        var wrongExtension = CreateReader().OpenAndValidate([1, 2, 3], "consultores.xlsm");
        Assert.Equal("Apenas são aceites ficheiros .xlsx.", wrongExtension.ErrorMessage);

        var malformed = CreateReader().OpenAndValidate([1, 2, 3], "consultores.xlsx");
        Assert.Equal("O ficheiro carregado não é um ficheiro .xlsx válido.", malformed.ErrorMessage);

        var tooLarge = CreateReader(new ImportLimitsOptions { MaxUploadBytes = 2 }).OpenAndValidate([1, 2, 3], "consultores.xlsx");
        Assert.StartsWith("O ficheiro excede o tamanho máximo permitido", tooLarge.ErrorMessage);
    }

    [Fact]
    public void GetDataRows_IgnoresTrailingBlankRows()
    {
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue("FullName");
            sheet.Cell(2, 1).SetValue("Maria");
            // Formatting-only row: bold style but no value, several rows below.
            sheet.Cell(5, 1).Style.Font.Bold = true;
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;

        var rows = ExcelWorkbookReader.GetDataRows(opened.Value.Worksheet).ToList();

        Assert.Single(rows);
        Assert.Equal(2, rows[0].RowNumber());
    }

    [Fact]
    public void IsFormula_DetectsFormulaCell()
    {
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue("FullName");
            sheet.Cell(2, 1).FormulaA1 = "=1+1";
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;
        var row = ExcelWorkbookReader.GetDataRows(opened.Value.Worksheet).Single();

        Assert.True(ExcelWorkbookReader.IsFormula(row, 1));
    }

    [Fact]
    public void ReadIdentifierText_TextCell_PreservesLeadingZeros()
    {
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue("NIF");
            var cell = sheet.Cell(2, 1);
            cell.SetValue("000123456");
            cell.Style.NumberFormat.Format = "@";
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;
        var row = ExcelWorkbookReader.GetDataRows(opened.Value.Worksheet).Single();

        var text = CreateReader().ReadIdentifierText(row, 1, out var ambiguous, out var tooLong);

        Assert.Equal("000123456", text);
        Assert.False(ambiguous);
        Assert.False(tooLong);
    }

    [Fact]
    public void ReadIdentifierText_NumberCell_IsFlaggedAmbiguous()
    {
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue("NIF");
            sheet.Cell(2, 1).SetValue(123456789);
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;
        var row = ExcelWorkbookReader.GetDataRows(opened.Value.Worksheet).Single();

        var text = CreateReader().ReadIdentifierText(row, 1, out var ambiguous, out _);

        Assert.Null(text);
        Assert.True(ambiguous);
    }

    [Fact]
    public void ReadText_CellLongerThanMaxCellLength_IsFlaggedTooLong()
    {
        var longValue = new string('x', 50);
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue("Notes");
            sheet.Cell(2, 1).SetValue(longValue);
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;
        var row = ExcelWorkbookReader.GetDataRows(opened.Value.Worksheet).Single();

        var reader = CreateReader(new ImportLimitsOptions { MaxCellLength = 10 });
        var text = reader.ReadText(row, 1, out var tooLong);

        Assert.Equal(longValue, text);
        Assert.True(tooLong);
    }

    [Fact]
    public void ReadDate_GenuineExcelDateCell_ReadDirectly()
    {
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue("Start");
            sheet.Cell(2, 1).Value = new DateTime(2026, 9, 15);
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;
        var row = ExcelWorkbookReader.GetDataRows(opened.Value.Worksheet).Single();

        var date = ExcelWorkbookReader.ReadDate(row, 1, out var invalid);

        Assert.Equal(new DateOnly(2026, 9, 15), date);
        Assert.False(invalid);
    }

    [Fact]
    public void ReadDate_TextCellDdMmYyyy_ParsesExplicitly()
    {
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue("Start");
            sheet.Cell(2, 1).SetValue("15/09/2026");
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;
        var row = ExcelWorkbookReader.GetDataRows(opened.Value.Worksheet).Single();

        var date = ExcelWorkbookReader.ReadDate(row, 1, out var invalid);

        Assert.Equal(new DateOnly(2026, 9, 15), date);
        Assert.False(invalid);
    }

    [Theory]
    [InlineData("2026-09-15")]
    [InlineData("09/15/2026")]
    [InlineData("15-09-2026")]
    [InlineData("not a date")]
    public void ReadDate_AmbiguousOrMalformedText_IsFlaggedInvalid(string text)
    {
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue("Start");
            sheet.Cell(2, 1).SetValue(text);
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;
        var row = ExcelWorkbookReader.GetDataRows(opened.Value.Worksheet).Single();

        var date = ExcelWorkbookReader.ReadDate(row, 1, out var invalid);

        Assert.Null(date);
        Assert.True(invalid);
    }

    [Fact]
    public void ReadDate_BlankCell_ReturnsNullWithoutInvalidFlag()
    {
        var bytes = BuildWorkbook(sheet =>
        {
            sheet.Cell(1, 1).SetValue("Start");
            sheet.Cell(1, 2).SetValue("End");
            sheet.Cell(2, 1).SetValue("15/09/2026");
        });
        var opened = CreateReader().OpenAndValidate(bytes, "x.xlsx");
        using var workbook = opened.Value.Workbook;
        var row = ExcelWorkbookReader.GetDataRows(opened.Value.Worksheet).Single();

        var date = ExcelWorkbookReader.ReadDate(row, 2, out var invalid);

        Assert.Null(date);
        Assert.False(invalid);
    }
}
