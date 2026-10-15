using ClosedXML.Excel;
using EquipmentManagement.Infrastructure.DataImports;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.DataImports;

/// <summary>
/// The Consultant and Equipment exports filter through native Excel Tables,
/// never a worksheet AutoFilter. In real Excel, a worksheet AutoFilter that is
/// used, cleared and saved leaves a plain <c>_FilterDatabase</c> name next to
/// Excel's built-in one, and every later open of the saved file stops on
/// "Nomes em conflito"; a Table keeps its filter inside the table. These tests
/// pin that structure, and that the change kept the sheets' data, styling and
/// formula-injection defense as they were.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class ExcelExportFilterTableTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string[] ConsultantsHeaders =
    [
        "ID do consultor", "Nome completo", "NIF", "Email", "Telefone", "Morada", "Ativo",
        "Regime atual", "Tipo de vínculo atual", "Função atual", "Empresa / Cliente atual",
        "Início da alocação atual", "Fim da alocação atual",
    ];

    private static readonly string[] AllocationsHeaders =
    [
        "ID do consultor", "Consultor", "Empresa / Cliente", "Regime", "Tipo de vínculo", "Função", "Data de início", "Data de fim", "Estado",
    ];

    private static readonly string[] EquipmentHeaders =
    [
        "ID do equipamento", "Código do equipamento", "Tipo de equipamento", "Categoria", "Fabricante", "Modelo", "Número de série", "Estado",
        "Processador", "Memória RAM", "Armazenamento", "Consultor atual", "Início da atribuição atual",
    ];

    [Fact]
    public async Task ConsultantExport_FiltersThroughExcelTables_KeepingDataStylingAndFormulaDefense()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var token = NewToken();
        var (_, company) = await Phase2TestData.CreateCompanyAsync(client, $"=Cliente Fórmula {token}");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var external = InventoryPerson.NewExternal(company, $"João Amostra {token}");
        var internalPerson = InventoryPerson.NewInternal($"=Inês Amostra {token}") with { Role = "=SUM(1,2)" };
        await CurrentInventoryTestData.PreviewAndConfirmAsync(client,
            [InventoryRow.Generic(type.Name, external), InventoryRow.Generic(type.Name, internalPerson)]);

        using var workbook = Reopen(await ExportAsync(client, $"/api/exports/consultants?search={token}"));

        AssertNoFilterDatabaseNames(workbook);
        var consultants = workbook.Worksheet(ConsultantExportService.ConsultantsSheetName);
        var allocations = workbook.Worksheet(ConsultantExportService.AllocationsSheetName);
        AssertFilterTable(consultants, ConsultantExportService.ConsultantsTableName, ConsultantsHeaders, "A1:M3");
        AssertFilterTable(allocations, ConsultantExportService.AllocationsTableName, AllocationsHeaders, "A1:I3");
        Assert.Equal(2, workbook.Worksheets.SelectMany(s => s.Tables).Select(t => t.Name).Distinct().Count());

        var externalRow = RowWhere(consultants, 2, external.FullName);
        Assert.Equal("Externo", externalRow.Cell(8).GetString());
        Assert.Equal(company, externalRow.Cell(11).GetString());
        Assert.False(externalRow.Cell(11).HasFormula);
        Assert.Equal(new DateTime(2025, 3, 3), externalRow.Cell(12).GetDateTime());
        Assert.True(externalRow.Cell(13).IsEmpty()); // no allocation end date

        var internalRow = RowWhere(consultants, 2, internalPerson.FullName);
        Assert.False(internalRow.Cell(2).HasFormula);
        Assert.Equal("=SUM(1,2)", internalRow.Cell(10).GetString());
        Assert.False(internalRow.Cell(10).HasFormula);
        Assert.Equal(XLDataType.Text, internalRow.Cell(10).DataType);
        Assert.Empty(internalRow.Cell(11).GetString()); // internal: no company
        Assert.Equal(internalPerson.Nif, internalRow.Cell(3).GetString());
        Assert.Equal(XLDataType.Text, internalRow.Cell(3).DataType);

        Assert.DoesNotContain(workbook.Worksheets.SelectMany(s => s.CellsUsed()), c => c.HasFormula);
    }

    [Fact]
    public async Task EquipmentExport_FiltersThroughAnExcelTable_KeepingDataStylingAndFormulaDefense()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var token = NewToken();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop");
        var holder = InventoryPerson.NewInternal($"Sónia Exemplo {token}");
        var assigned = InventoryRow.Laptop(type.Name, holder, serial: $"EX{token}01", assignmentDate: new DateOnly(2025, 6, 2))
            with { Model = "ThinkPad T14 Geração 4" };
        var stock = InventoryRow.Laptop(type.Name, serial: $"EX{token}02")
            with { Manufacturer = "=HYPERLINK(\"http://example.invalid\",\"x\")", Model = "@Modelo", Processor = "+1" };
        await CurrentInventoryTestData.PreviewAndConfirmAsync(client, [assigned, stock]);

        using var workbook = Reopen(await ExportAsync(client, $"/api/exports/equipment?search=EX{token}"));

        AssertNoFilterDatabaseNames(workbook);
        var sheet = workbook.Worksheet(EquipmentExportService.SheetName);
        AssertFilterTable(sheet, EquipmentExportService.TableName, EquipmentHeaders, "A1:M3");

        var assignedRow = RowWhere(sheet, 7, assigned.SerialNumber!);
        Assert.Equal("ThinkPad T14 Geração 4", assignedRow.Cell(6).GetString());
        Assert.Equal(holder.FullName, assignedRow.Cell(12).GetString());
        Assert.Equal(XLDataType.DateTime, assignedRow.Cell(13).DataType);

        var stockRow = RowWhere(sheet, 7, stock.SerialNumber!);
        Assert.Equal(stock.Manufacturer, stockRow.Cell(5).GetString());
        Assert.Equal("@Modelo", stockRow.Cell(6).GetString());
        Assert.Equal("+1", stockRow.Cell(9).GetString());
        Assert.Equal(XLDataType.Text, stockRow.Cell(5).DataType);
        Assert.Equal(XLDataType.Text, stockRow.Cell(6).DataType);
        Assert.Equal(XLDataType.Text, stockRow.Cell(9).DataType);
        Assert.Empty(stockRow.Cell(12).GetString()); // no active assignment
        Assert.True(stockRow.Cell(13).IsEmpty());

        Assert.DoesNotContain(sheet.CellsUsed(), c => c.HasFormula);
    }

    [Fact]
    public async Task EmptyExports_HaveOnlyTheStyledHeader_NoTableAndNoArtificialRow()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nothing = NewToken();

        using var consultants = Reopen(await ExportAsync(client, $"/api/exports/consultants?search={nothing}"));
        AssertNoFilterDatabaseNames(consultants);
        AssertHeaderOnly(consultants.Worksheet(ConsultantExportService.ConsultantsSheetName), ConsultantsHeaders);
        AssertHeaderOnly(consultants.Worksheet(ConsultantExportService.AllocationsSheetName), AllocationsHeaders);

        using var equipment = Reopen(await ExportAsync(client, $"/api/exports/equipment?search={nothing}"));
        AssertNoFilterDatabaseNames(equipment);
        AssertHeaderOnly(equipment.Worksheet(EquipmentExportService.SheetName), EquipmentHeaders);
    }

    [Fact]
    public async Task SingleRowExport_StillFiltersThroughATable()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var token = NewToken();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        await CurrentInventoryTestData.PreviewAndConfirmAsync(client,
            [InventoryRow.Generic(type.Name, InventoryPerson.NewInternal($"Única Pessoa {token}"), serial: $"EX{token}01")]);

        using var consultants = Reopen(await ExportAsync(client, $"/api/exports/consultants?search={token}"));
        AssertFilterTable(consultants.Worksheet(1), ConsultantExportService.ConsultantsTableName, ConsultantsHeaders, "A1:M2");
        AssertFilterTable(consultants.Worksheet(2), ConsultantExportService.AllocationsTableName, AllocationsHeaders, "A1:I2");

        using var equipment = Reopen(await ExportAsync(client, $"/api/exports/equipment?search=EX{token}"));
        AssertFilterTable(equipment.Worksheet(1), EquipmentExportService.TableName, EquipmentHeaders, "A1:M2");
        Assert.Equal($"EX{token}01", equipment.Worksheet(1).Cell(2, 7).GetString());
    }

    /// <summary>Zero exported entities: the header row only — no Table body row and no filter (nothing to filter), styling intact.</summary>
    private static void AssertHeaderOnly(IXLWorksheet sheet, string[] headers)
    {
        Assert.Empty(sheet.Tables);
        Assert.False(sheet.AutoFilter.IsEnabled);
        Assert.Equal(1, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(1, sheet.SheetView.SplitRow);
        Assert.All(Enumerable.Range(1, headers.Length), i =>
        {
            var cell = sheet.Cell(1, i);
            Assert.Equal(headers[i - 1], cell.GetString());
            Assert.True(cell.Style.Font.Bold);
            Assert.Equal(XLColor.FromHtml("#1F3864"), cell.Style.Fill.BackgroundColor);
            Assert.True(sheet.Column(i).Width >= 14);
        });
    }

    private static void AssertNoFilterDatabaseNames(XLWorkbook workbook)
    {
        Assert.DoesNotContain(workbook.DefinedNames, n => n.Name.Contains("FilterDatabase", StringComparison.OrdinalIgnoreCase));
        Assert.All(workbook.Worksheets, s =>
            Assert.DoesNotContain(s.DefinedNames, n => n.Name.Contains("FilterDatabase", StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssertFilterTable(IXLWorksheet sheet, string tableName, string[] headers, string range)
    {
        Assert.False(sheet.AutoFilter.IsEnabled);
        var table = Assert.Single(sheet.Tables);
        Assert.Equal(tableName, table.Name);
        Assert.Equal(range, table.RangeAddress.ToString());
        Assert.Equal(headers, table.Fields.Select(f => f.Name).ToArray());
        Assert.True(table.ShowHeaderRow);
        Assert.True(table.ShowAutoFilter);
        Assert.False(table.ShowTotalsRow);
        Assert.Equal(XLTableTheme.None, table.Theme);
        Assert.False(table.ShowRowStripes);

        // The export's own header look and frozen header row are unchanged.
        Assert.Equal(1, sheet.SheetView.SplitRow);
        Assert.All(Enumerable.Range(1, headers.Length), i =>
        {
            var cell = sheet.Cell(1, i);
            Assert.Equal(headers[i - 1], cell.GetString());
            Assert.True(cell.Style.Font.Bold);
            Assert.Equal(XLColor.FromHtml("#1F3864"), cell.Style.Fill.BackgroundColor);
            Assert.True(sheet.Column(i).Width >= 14);
        });
    }

    private static IXLRow RowWhere(IXLWorksheet sheet, int column, string value) =>
        Assert.Single(sheet.RowsUsed().Skip(1), r => r.Cell(column).GetString() == value);

    private static async Task<byte[]> ExportAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>Opens the export, saves it again and reopens the saved copy — a library-level round trip, which supplements (never replaces) the real-Excel check.</summary>
    private static XLWorkbook Reopen(byte[] bytes)
    {
        using var first = new XLWorkbook(new MemoryStream(bytes));
        var resaved = new MemoryStream();
        first.SaveAs(resaved);
        resaved.Position = 0;
        return new XLWorkbook(resaved);
    }

    private static string NewToken() => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
