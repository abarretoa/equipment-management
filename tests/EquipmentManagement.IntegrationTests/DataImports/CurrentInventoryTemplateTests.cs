using ClosedXML.Excel;
using EquipmentManagement.Infrastructure.ClientProfiles;
using EquipmentManagement.Infrastructure.DataImports;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.DataImports;

/// <summary>
/// The generated "Modelo_Importacao_Inventario_Atual.xlsx", read back exactly
/// as served by the real endpoint: pt-PT sheets/headers/instructions, the
/// established template styling, formats, dropdowns, and that an untouched
/// template is a valid, empty import.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class CurrentInventoryTemplateTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    /// <summary>Named after the deployment's mobile operator when its client profile has one.</summary>
    private static readonly string MobileNumberHeader = ResponsibilityTermText.MobileNumberImportHeader(ClientProfileLoader.Embedded.Identity);

    private static readonly string[] ExpectedHeaders =
    [
        "Tipo de equipamento", "Fabricante", "Modelo", "Número de série", "Observações", "Processador", "Memória RAM", "Armazenamento",
        "IMEI", MobileNumberHeader, "N.º do cartão SIM",
        "Nome completo", "NIF", "Email", "Telefone", "Morada", "Regime", "Tipo de vínculo", "Função", "Empresa / Cliente",
        "Data de início", "Data de fim", "Data de atribuição",
    ];

    [Fact]
    public async Task Template_HasThePtPtStructure_Styling_FormatsAndDropdowns()
    {
        using var client = await LoginAsync();
        var response = await client.GetAsync($"{CurrentInventoryTestData.BaseUrl}/template");
        response.EnsureSuccessStatusCode();
        Assert.Equal("Modelo_Importacao_Inventario_Atual.xlsx", response.Content.Headers.ContentDisposition!.FileNameStar);

        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        Assert.Equal(["Inventário atual", "Instruções"],
            workbook.Worksheets.Where(w => w.Visibility == XLWorksheetVisibility.Visible).Select(w => w.Name).ToArray());
        Assert.Equal("Inventário atual", workbook.Worksheets.First().Name);

        var sheet = workbook.Worksheet("Inventário atual");
        Assert.Equal(ExpectedHeaders, Enumerable.Range(1, ExpectedHeaders.Length).Select(i => sheet.Cell(1, i).GetString()).ToArray());
        Assert.True(sheet.Cell(1, ExpectedHeaders.Length + 1).IsEmpty());

        // Same convention as the other templates: the strong fill marks only
        // the always-required field; every other header uses the soft fill.
        var strong = sheet.Cell(1, 1).Style.Fill.BackgroundColor;
        var soft = sheet.Cell(1, 2).Style.Fill.BackgroundColor;
        Assert.NotEqual(strong, soft);
        Assert.All(Enumerable.Range(2, ExpectedHeaders.Length - 1), i => Assert.Equal(soft, sheet.Cell(1, i).Style.Fill.BackgroundColor));
        Assert.True(sheet.Cell(1, 1).Style.Font.Bold);

        Assert.Equal(1, sheet.SheetView.SplitRow);

        // Filtering comes only from the Excel Table: no worksheet AutoFilter
        // (whose _FilterDatabase name Excel duplicates after re-filtering,
        // making the next open stop on "Nomes em conflito").
        Assert.False(sheet.AutoFilter.IsEnabled);
        Assert.DoesNotContain(workbook.DefinedNames, n => n.Name.Contains("FilterDatabase", StringComparison.OrdinalIgnoreCase));
        var table = Assert.Single(sheet.Tables);
        Assert.Equal(CurrentInventoryImportService.InventoryTableName, table.Name);
        Assert.Equal("A1:W2", table.RangeAddress.ToString());
        Assert.Equal(ExpectedHeaders, table.Fields.Select(f => f.Name).ToArray());
        Assert.True(table.ShowAutoFilter);
        Assert.True(table.ShowHeaderRow);
        Assert.Equal(XLTableTheme.None, table.Theme);
        Assert.False(table.ShowRowStripes);
        Assert.All(Enumerable.Range(1, ExpectedHeaders.Length), i => Assert.True(sheet.Column(i).Width >= 13));

        foreach (var dateHeader in new[] { "Data de início", "Data de fim", "Data de atribuição" })
        {
            Assert.Equal("dd/MM/yyyy", sheet.Cell(2, Column(dateHeader)).Style.DateFormat.Format);
        }

        foreach (var textHeader in new[] { "Número de série", "IMEI", MobileNumberHeader, "N.º do cartão SIM", "NIF", "Telefone" })
        {
            Assert.Equal("@", sheet.Cell(2, Column(textHeader)).Style.NumberFormat.Format);
        }

        Assert.True(sheet.Cell(2, Column("Observações")).Style.Alignment.WrapText);
        Assert.False(sheet.Cell(1, Column("Observações")).Style.Alignment.WrapText);

        var lists = sheet.DataValidations.Where(v => v.AllowedValues == XLAllowedValues.List).ToList();
        Assert.Contains(lists, v => v.Value == "\"Interno,Externo\"" && v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == Column("Regime")));
        Assert.Contains(lists, v => v.Value == "\"Prestador de serviço,Contrato de trabalho\"" && v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == Column("Tipo de vínculo")));
        Assert.DoesNotContain(lists, v => v.Value.Contains('0', StringComparison.Ordinal) || v.Value.Contains('1', StringComparison.Ordinal));

        // No example/fake record in the data rows.
        Assert.Single(sheet.RowsUsed());
    }

    [Fact]
    public async Task Template_InstructionsArePtPt_WithTheRequiredGuidance_AndNoInternalNames()
    {
        using var client = await LoginAsync();
        var bytes = await client.GetByteArrayAsync($"{CurrentInventoryTestData.BaseUrl}/template");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var instructions = workbook.Worksheet("Instruções");
        var text = string.Join("\n", instructions.CellsUsed().Select(c => c.GetString()));

        foreach (var expected in new[]
                 {
                     "Cada linha corresponde a um equipamento",
                     "cria-a uma única vez",
                     $"Regime = Interno significa funcionário interno da {ClientProfileLoader.Embedded.Identity.OrganizationName}",
                     "Regime = Externo significa consultor externo",
                     "Prestador de serviço e Contrato de trabalho",
                     "não deduz o Regime nem o Tipo de vínculo",
                     "fica Disponível",
                     "gerado automaticamente pela plataforma",
                     "não representa uma nova entrega",
                     "não envia emails",
                     "não cria Termos de Responsabilidade",
                     "sem fotografias",
                     "o IMEI é obrigatório",
                     "Nunca indique o PIN, o PUK",
                     "DD/MM/AAAA",
                     "Data de atribuição",
                     "vínculo", "série", "telemóvel", "início", "serviço", "Memória", "Observações", "funcionário", "Disponível",
                 })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        // HR never sees technical/English field names in either sheet.
        var dataSheetText = workbook.Worksheet("Inventário atual").CellsUsed().Select(c => c.GetString()).ToList();
        var allText = text + "\n" + string.Join("\n", dataSheetText);
        foreach (var internalName in new[]
                 {
                     "EquipmentType", "SerialNumber", "FullName", "WorkforceType", "EngagementType", "RoleOrFunction", "AllocationStartDate",
                     "AssignmentDate", "MobileNumber", "SimSerialNumber", "Internal", "External", "ServiceProvider", "EmploymentContract",
                     "DTO", "enum", "transaction", "Infrastructure", "AssetTag",
                 })
        {
            Assert.DoesNotContain(internalName, allText, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(dataSheetText, h => h.Contains("PIN", StringComparison.OrdinalIgnoreCase) || h.Contains("PUK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UntouchedTemplate_PreviewsAsAValidEmptyImport_AndTheHiddenListIsNotImportData()
    {
        using var client = await LoginAsync();
        // At least one active type, so the template really carries a non-empty "Listas" sheet.
        await Phase3TestData.CreateEquipmentTypeAsync(client, name: $"Monitor Vazio {Guid.NewGuid():N}"[..20], kind: "Generic", assetTagPrefix: "MN");
        var bytes = await client.GetByteArrayAsync($"{CurrentInventoryTestData.BaseUrl}/template");
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            Assert.NotEmpty(workbook.Worksheet(CurrentInventoryImportService.ListsSheetName).CellsUsed());
        }

        var preview = await CurrentInventoryTestData.PreviewAsync(client, bytes);

        Assert.Equal(0, preview.TotalRows);
        Assert.Equal(0, preview.ErrorRows);
        Assert.Empty(preview.Rows);
    }

    [Fact]
    public async Task Template_SheetOrder_IsData_Instructions_ThenTheVeryHiddenList()
    {
        using var client = await LoginAsync();
        await Phase3TestData.CreateEquipmentTypeAsync(client, name: $"Rato Ordem {Guid.NewGuid():N}"[..19], kind: "Generic", assetTagPrefix: "RT");

        using var workbook = new XLWorkbook(new MemoryStream(await client.GetByteArrayAsync($"{CurrentInventoryTestData.BaseUrl}/template")));

        Assert.Equal(["Inventário atual", "Instruções", "Listas"], workbook.Worksheets.Select(w => w.Name).ToArray());
        Assert.Equal(XLWorksheetVisibility.Visible, workbook.Worksheet(1).Visibility);
        Assert.Equal(XLWorksheetVisibility.Visible, workbook.Worksheet(2).Visibility);
        Assert.Equal(XLWorksheetVisibility.VeryHidden, workbook.Worksheet(3).Visibility);
        Assert.True(workbook.Worksheet(1).TabActive);
    }

    [Fact]
    public async Task Template_WithNoActiveEquipmentTypes_HasNoTypeDropdown_AndTellsHrWhatToDo()
    {
        using var client = await LoginAsync();

        // The integration collection runs sequentially, so every active type can
        // be archived for this one request and restored straight afterwards.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var activeIds = await db.EquipmentTypes.Where(t => t.IsActive).Select(t => t.Id).ToListAsync();
        byte[] bytes;
        try
        {
            await db.EquipmentTypes.Where(t => activeIds.Contains(t.Id)).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false));
            var response = await client.GetAsync($"{CurrentInventoryTestData.BaseUrl}/template");
            response.EnsureSuccessStatusCode();
            bytes = await response.Content.ReadAsByteArrayAsync();
        }
        finally
        {
            await db.EquipmentTypes.Where(t => activeIds.Contains(t.Id)).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, true));
        }

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["Inventário atual", "Instruções"], workbook.Worksheets.Select(w => w.Name).ToArray());
        Assert.DoesNotContain(workbook.DefinedNames, n => n.Name == CurrentInventoryImportService.EquipmentTypeListName);
        Assert.DoesNotContain(workbook.Worksheet(1).DataValidations, v => v.Value == CurrentInventoryImportService.EquipmentTypeListName);
        Assert.Equal(2, workbook.Worksheet(1).DataValidations.Count()); // Regime + Tipo de vínculo only

        var instructions = string.Join("\n", workbook.Worksheet("Instruções").CellsUsed().Select(c => c.GetString()));
        Assert.Contains("Ainda não existe nenhum tipo de equipamento ativo na plataforma", instructions, StringComparison.Ordinal);
        Assert.Contains("crie ou reative os tipos necessários", instructions, StringComparison.Ordinal);
        Assert.Contains("descarregue o modelo novamente", instructions, StringComparison.Ordinal);
    }

    private static int Column(string header) => Array.IndexOf(ExpectedHeaders, header) + 1;

    [Fact]
    public async Task Template_EquipmentTypeCells_HaveADropdownOfTheActiveTypes_FromAVeryHiddenList()
    {
        using var client = await LoginAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var active = await Phase3TestData.CreateEquipmentTypeAsync(client, name: $"Portátil Lista {suffix}", kind: "Laptop", assetTagPrefix: "PT");
        var archived = await Phase3TestData.CreateEquipmentTypeAsync(client, name: $"Tipo Arquivado {suffix}", kind: "Generic", assetTagPrefix: "TA");
        var csrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync<object?>($"/api/equipment-types/{archived.Id}/deactivate", null, csrf)).EnsureSuccessStatusCode();

        var bytes = await client.GetByteArrayAsync($"{CurrentInventoryTestData.BaseUrl}/template");
        using var workbook = new XLWorkbook(new MemoryStream(bytes));

        // The support list: last sheet, very hidden (cannot be unhidden from Excel's normal menu).
        var lists = workbook.Worksheet(CurrentInventoryImportService.ListsSheetName);
        Assert.Equal(XLWorksheetVisibility.VeryHidden, lists.Visibility);
        Assert.Same(lists, workbook.Worksheets.Last());

        // Built from the database at generation time: the type created by this
        // test is offered, the archived one is not — nothing is hard-coded.
        var offered = lists.Column(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains(active.Name, offered);
        Assert.DoesNotContain(archived.Name, offered);

        var name = Assert.Single(workbook.DefinedNames, n => n.Name == CurrentInventoryImportService.EquipmentTypeListName);
        Assert.Equal($"{lists.Name}!$A$1:$A${offered.Count}", name.RefersTo.Replace("'", string.Empty, StringComparison.Ordinal));

        // The cell dropdown covers the data cells (not the header) and is strict.
        var sheet = workbook.Worksheet("Inventário atual");
        var typeValidation = Assert.Single(sheet.DataValidations, v => v.Value == CurrentInventoryImportService.EquipmentTypeListName);
        Assert.Equal(XLAllowedValues.List, typeValidation.AllowedValues);
        Assert.True(typeValidation.InCellDropdown);
        Assert.Equal(XLErrorStyle.Stop, typeValidation.ErrorStyle);
        Assert.True(typeValidation.ShowErrorMessage);
        var range = Assert.Single(typeValidation.Ranges).RangeAddress;
        Assert.Equal("A2:A2001", range.ToString());
        Assert.True(sheet.Cell(1, 1).GetDataValidation() is null || sheet.Cell(1, 1).GetDataValidation().Value != CurrentInventoryImportService.EquipmentTypeListName);

        // The header filter is the Table's, separately from the cell dropdown.
        Assert.True(Assert.Single(sheet.Tables).ShowAutoFilter);
        Assert.False(sheet.AutoFilter.IsEnabled);
    }

    [Fact]
    public async Task TableBasedTemplate_FilledLikeHrWould_PreviewsEachRowExactlyOnce_AndIgnoresBlankRows()
    {
        using var client = await LoginAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var generic = await Phase3TestData.CreateEquipmentTypeAsync(client, name: $"Monitor Tabela {suffix}", kind: "Generic", assetTagPrefix: "MN");
        var bytes = await client.GetByteArrayAsync($"{CurrentInventoryTestData.BaseUrl}/template");

        // Fill the generated template the way Excel does when HR types below
        // the Table: the Table grows to cover the new rows. Row 3 is left
        // fully blank inside the Table and must be ignored.
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Inventário atual");
        var serials = new[] { InventoryRow.NewSerial(), InventoryRow.NewSerial(), InventoryRow.NewSerial() };
        foreach (var (rowNumber, serial) in new[] { (2, serials[0]), (4, serials[1]), (5, serials[2]) })
        {
            sheet.Cell(rowNumber, 1).SetValue(generic.Name);
            sheet.Cell(rowNumber, 2).SetValue("Dell");
            sheet.Cell(rowNumber, 4).SetValue(serial);
        }

        var table = sheet.Tables.Single();
        table.Resize(sheet.Range(1, 1, 5, ExpectedHeaders.Length));
        using var filled = new MemoryStream();
        workbook.SaveAs(filled);

        var preview = await CurrentInventoryTestData.PreviewAsync(client, filled.ToArray());

        Assert.Equal(3, preview.TotalRows);
        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal([2, 4, 5], preview.Rows.Select(r => r.RowNumber).ToArray());
        Assert.Equal(serials, preview.Rows.Select(r => r.SerialNumber).ToArray());
        Assert.Equal(3, preview.Summary.EquipmentToCreate);
    }

    private async Task<HttpClient> LoginAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }

    [Fact]
    public void ColumnDefinitions_MatchTheTemplateHeaders()
    {
        Assert.Equal(ExpectedHeaders, CurrentInventoryImportColumns.All.Select(c => c.Header).ToArray());
    }
}
