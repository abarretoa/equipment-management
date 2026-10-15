using System.IO.Compression;
using ClosedXML.Excel;
using EquipmentManagement.Infrastructure.DataImports;

namespace EquipmentManagement.UnitTests.Infrastructure.DataImports;

/// <summary>
/// The generated import templates (ADR-070): pt-PT sheet names, headers in
/// the documented order, dropdown values, instructions, and nothing internal
/// in any visible cell. Read back through ClosedXML and, where the raw file
/// matters, through the package XML itself — never compared byte-for-byte.
/// </summary>
public class ImportTemplateTests
{
    /// <summary>Internal C#/enum names that must never appear in a cell of a generated template.</summary>
    private static readonly string[] InternalNames =
    [
        "FullName", "Phone", "Address", "WorkforceType", "EngagementType", "RoleOrFunction", "Company", "AllocationStartDate",
        "AllocationEndDate", "EquipmentType", "Manufacturer", "SerialNumber", "Notes", "Processor", "Memory", "Storage",
        "Internal", "External", "ServiceProvider", "EmploymentContract", "AssetTag", "Laptop", "Generic", "MobilePhone",
        "Instructions", "Consultants", "Equipment",
    ];

    private static readonly string[] AlwaysRequiredConsultantHeaders = ["Nome completo", "NIF", "Email", "Telefone", "Morada"];
    private static readonly string[] LaptopOnlyEquipmentHeaders = ["Fabricante", "Número de série", "Processador", "Memória RAM", "Armazenamento"];
    private static readonly string[] AccentedConsultantTexts = ["Função", "Tipo de vínculo", "Data de início", "Instruções", "Importação de consultores"];
    private static readonly string[] AccentedEquipmentTexts = ["Número de série", "Observações", "Memória RAM", "Telemóvel", "atribuição"];

    [Fact]
    public void ConsultantTemplate_HasPortugueseSheetsAndHeaders_InOrder()
    {
        using var workbook = Open(ConsultantImportService.GenerateTemplate());

        Assert.Equal(["Consultores", "Instruções"], workbook.Worksheets.Select(s => s.Name));
        Assert.Equal(
            ["Nome completo", "NIF", "Email", "Telefone", "Morada", "Regime", "Tipo de vínculo", "Função", "Empresa / Cliente", "Data de início", "Data de fim"],
            HeaderRow(workbook.Worksheet(1)));
    }

    [Fact]
    public void EquipmentTemplate_HasPortugueseSheetsAndHeaders_InOrder()
    {
        using var workbook = Open(EquipmentImportService.GenerateTemplate());

        Assert.Equal(["Equipamentos", "Instruções"], workbook.Worksheets.Select(s => s.Name));
        Assert.Equal(
            ["Tipo de equipamento", "Fabricante", "Modelo", "Número de série", "Observações", "Processador", "Memória RAM", "Armazenamento"],
            HeaderRow(workbook.Worksheet(1)));
    }

    [Fact]
    public void Templates_ShowNoInternalNameInAnyCell()
    {
        foreach (var bytes in new[] { ConsultantImportService.GenerateTemplate(), EquipmentImportService.GenerateTemplate() })
        {
            using var workbook = Open(bytes);
            var texts = workbook.Worksheets.SelectMany(s => s.CellsUsed().Select(c => c.GetString())).ToList();

            Assert.NotEmpty(texts);
            foreach (var text in texts)
            {
                foreach (var word in text.Split([' ', '"', '.', ',', ':', '(', ')', '/'], StringSplitOptions.RemoveEmptyEntries))
                {
                    Assert.DoesNotContain(word, InternalNames);
                }
            }
        }
    }

    [Fact]
    public void Templates_DataSheetIsReadyToFill_WithNoRowThatWouldBeImported()
    {
        foreach (var bytes in new[] { ConsultantImportService.GenerateTemplate(), EquipmentImportService.GenerateTemplate() })
        {
            using var workbook = Open(bytes);
            var sheet = workbook.Worksheet(1);

            // The example lives on the instructions sheet: uploading the template untouched imports nothing.
            Assert.Empty(ExcelWorkbookReader.GetDataRows(sheet));
            Assert.Equal(1, sheet.SheetView.SplitRow);
            Assert.All(sheet.ColumnsUsed(), column => Assert.True(column.Width >= 14, $"Column {column.ColumnNumber()} is too narrow to read."));
        }
    }

    [Fact]
    public void ConsultantTemplate_Dropdowns_OfferThePortugueseValues_AndAllowBlank()
    {
        using var workbook = Open(ConsultantImportService.GenerateTemplate());
        var validations = workbook.Worksheet(1).DataValidations.ToList();

        Assert.Equal(2, validations.Count);
        var regime = validations.Single(v => v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == 6));
        var engagement = validations.Single(v => v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == 7));

        Assert.Equal(XLAllowedValues.List, regime.AllowedValues);
        Assert.Equal("\"Interno,Externo\"", regime.Value);
        Assert.True(regime.IgnoreBlanks);
        Assert.True(regime.InCellDropdown);

        Assert.Equal(XLAllowedValues.List, engagement.AllowedValues);
        Assert.Equal("\"Prestador de serviço,Contrato de trabalho\"", engagement.Value);
        Assert.True(engagement.IgnoreBlanks);
        Assert.True(engagement.InCellDropdown);
    }

    /// <summary>
    /// The raw rule as a spreadsheet application reads it. An inline list must
    /// be a quoted string: written unquoted, Excel takes it for a reference
    /// and will not open the workbook without repairing it.
    /// </summary>
    [Fact]
    public void ConsultantTemplate_DropdownRules_AreQuotedInlineLists_InTheRawWorkbook()
    {
        var sheetXml = ReadEntry(ConsultantImportService.GenerateTemplate(), "xl/worksheets/sheet1.xml");

        Assert.Contains("formula1>\"Interno,Externo\"</", sheetXml);
        Assert.Contains("formula1>\"Prestador de serviço,Contrato de trabalho\"</", sheetXml);
        Assert.Contains("allowBlank=\"1\"", sheetXml);
        Assert.DoesNotContain("formula1>Interno", sheetXml);
    }

    [Fact]
    public void EquipmentTemplate_HasNoDropdown_TheTypeIsMatchedByName()
    {
        using var workbook = Open(EquipmentImportService.GenerateTemplate());

        Assert.Empty(workbook.Worksheet(1).DataValidations);
    }

    [Fact]
    public void ConsultantTemplate_IdentifierAndDateColumns_KeepTheirFormats()
    {
        using var workbook = Open(ConsultantImportService.GenerateTemplate());
        var sheet = workbook.Worksheet(1);

        Assert.Equal("@", sheet.Column(2).Style.NumberFormat.Format); // NIF
        Assert.Equal("@", sheet.Column(4).Style.NumberFormat.Format); // Telefone
        Assert.Equal("dd/MM/yyyy", sheet.Column(10).Style.DateFormat.Format); // Data de início
        Assert.Equal("dd/MM/yyyy", sheet.Column(11).Style.DateFormat.Format); // Data de fim
    }

    [Fact]
    public void EquipmentTemplate_SerialNumberColumn_StaysText()
    {
        using var workbook = Open(EquipmentImportService.GenerateTemplate());

        Assert.Equal("@", workbook.Worksheet(1).Column(4).Style.NumberFormat.Format);
    }

    [Fact]
    public void ConsultantTemplate_Instructions_SayWhichFieldsAreRequired_AndHowToFillThem()
    {
        using var workbook = Open(ConsultantImportService.GenerateTemplate());
        var guide = ReadGuide(workbook.Worksheet("Instruções"));

        Assert.Equal(
            ["Nome completo", "NIF", "Email", "Telefone", "Morada", "Regime", "Tipo de vínculo", "Função", "Empresa / Cliente", "Data de início", "Data de fim"],
            guide.Keys);
        Assert.All(AlwaysRequiredConsultantHeaders, header => Assert.Equal("Sim", guide[header].Required));
        Assert.Equal("Se indicar uma alocação", guide["Regime"].Required);
        Assert.Equal("Se o regime for Externo", guide["Empresa / Cliente"].Required);
        Assert.Equal("Não", guide["Data de fim"].Required);
        Assert.Contains("Interno ou Externo", guide["Regime"].Guidance);
        Assert.Contains("Prestador de serviço ou Contrato de trabalho", guide["Tipo de vínculo"].Guidance);
        Assert.Contains("DD/MM/AAAA", guide["Data de início"].Guidance);
        Assert.Equal("15/01/2026", guide["Data de início"].Example);
    }

    [Fact]
    public void ConsultantTemplate_AlwaysRequiredFields_AreTheOnesWithTheStrongHeader()
    {
        using var workbook = Open(ConsultantImportService.GenerateTemplate());
        var sheet = workbook.Worksheet(1);

        var strong = sheet.Cell(1, 1).Style.Fill.BackgroundColor;
        Assert.All(Enumerable.Range(1, 5), column => Assert.Equal(strong, sheet.Cell(1, column).Style.Fill.BackgroundColor));
        Assert.All(Enumerable.Range(6, 6), column => Assert.NotEqual(strong, sheet.Cell(1, column).Style.Fill.BackgroundColor));
    }

    [Fact]
    public void EquipmentTemplate_Instructions_SayWhichFieldsAreRequired_AndWhatIsNotImported()
    {
        using var workbook = Open(EquipmentImportService.GenerateTemplate());
        var instructions = workbook.Worksheet("Instruções");
        var guide = ReadGuide(instructions);

        Assert.Equal(
            ["Tipo de equipamento", "Fabricante", "Modelo", "Número de série", "Observações", "Processador", "Memória RAM", "Armazenamento"], guide.Keys);
        Assert.Equal("Sim", guide["Tipo de equipamento"].Required);
        Assert.Equal("Não", guide["Modelo"].Required);
        Assert.All(
            LaptopOnlyEquipmentHeaders,
            header => Assert.Equal("Apenas para portáteis", guide[header].Required));

        var notes = string.Join("\n", instructions.CellsUsed().Select(c => c.GetString()));
        Assert.Contains("O código do equipamento é gerado automaticamente", notes);
        Assert.Contains("A atribuição a um consultor não é importada", notes);
        Assert.Contains("Não inclua PIN, PUK, palavras-passe ou outros dados confidenciais.", notes);
    }

    [Fact]
    public void Templates_KeepPortugueseAccents_InTheRawWorkbook()
    {
        var consultant = ConsultantImportService.GenerateTemplate();
        var strings = ReadEntry(consultant, "xl/sharedStrings.xml") + ReadEntry(consultant, "xl/workbook.xml");
        Assert.All(AccentedConsultantTexts, text => Assert.Contains(text, strings));

        var equipment = EquipmentImportService.GenerateTemplate();
        strings = ReadEntry(equipment, "xl/sharedStrings.xml") + ReadEntry(equipment, "xl/workbook.xml");
        Assert.All(AccentedEquipmentTexts, text => Assert.Contains(text, strings, StringComparison.OrdinalIgnoreCase));
    }

    private static XLWorkbook Open(byte[] bytes) => new(new MemoryStream(bytes));

    private static List<string> HeaderRow(IXLWorksheet sheet) =>
        [.. sheet.Row(1).CellsUsed().Select(c => c.GetString())];

    /// <summary>The "Coluna / Obrigatório / Como preencher / Exemplo" table of an instructions sheet, keyed by column header.</summary>
    private static Dictionary<string, (string Required, string Guidance, string Example)> ReadGuide(IXLWorksheet instructions)
    {
        var headerRow = instructions.RowsUsed().Single(r => r.Cell(1).GetString() == "Coluna");
        Assert.Equal(["Coluna", "Obrigatório", "Como preencher", "Exemplo"], headerRow.CellsUsed().Select(c => c.GetString()));

        return instructions.RowsUsed()
            .Where(r => r.RowNumber() > headerRow.RowNumber())
            .ToDictionary(r => r.Cell(1).GetString(), r => (r.Cell(2).GetString(), r.Cell(3).GetString(), r.Cell(4).GetString()));
    }

    private static string ReadEntry(byte[] workbook, string entryName)
    {
        using var archive = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry(entryName)!.Open());
        return reader.ReadToEnd();
    }
}
