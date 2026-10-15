using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.DataImports;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.DataImports;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.DataImports;

/// <summary>
/// The pt-PT Excel contract end to end (ADR-070): a newly downloaded template
/// can be filled in and imported; a workbook in the pre-localization English
/// format still imports; headers and enum values are matched by an explicit
/// alias list, never by similarity; and every message, header and exported
/// value a person reads is pt-PT.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class LocalizedExcelImportExportTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string[] ConsultantsSheetHeaders =
    [
        "ID do consultor", "Nome completo", "NIF", "Email", "Telefone", "Morada", "Ativo", "Regime atual", "Tipo de vínculo atual",
        "Função atual", "Empresa / Cliente atual", "Início da alocação atual", "Fim da alocação atual",
    ];

    private static readonly string[] AllocationsSheetHeaders =
        ["ID do consultor", "Consultor", "Empresa / Cliente", "Regime", "Tipo de vínculo", "Função", "Data de início", "Data de fim", "Estado"];

    private static readonly string[] EquipmentSheetHeaders =
    [
        "ID do equipamento", "Código do equipamento", "Tipo de equipamento", "Categoria", "Fabricante", "Modelo", "Número de série", "Estado",
        "Processador", "Memória RAM", "Armazenamento", "Consultor atual", "Início da atribuição atual",
    ];

    // ------------------------------------------------------------ Consultants

    [Fact]
    public async Task Consultants_DownloadedPortugueseTemplate_FilledIn_ImportsConsultantsAndAllocation()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var (nifWithAllocation, nifProfileOnly) = (NewNif(), NewNif());

        // The real template, exactly as a person downloads it, filled in the way a person would:
        // dropdown values as offered, and the date as a genuine Excel date.
        var bytes = await FillTemplateAsync(client, "/api/imports/consultants/template", "Consultores", sheet =>
        {
            Write(sheet, 2, "Nome completo", "Conceição Fictícia");
            Write(sheet, 2, "NIF", nifWithAllocation);
            Write(sheet, 2, "Email", "conceicao.ficticia@example.invalid");
            Write(sheet, 2, "Telefone", "+351900000101");
            Write(sheet, 2, "Morada", "Rua da Atribuição 12, 1000-001 Lisboa");
            Write(sheet, 2, "Regime", "Externo");
            Write(sheet, 2, "Tipo de vínculo", "Prestador de serviço");
            Write(sheet, 2, "Função", "Gestão de projeto");
            Write(sheet, 2, "Empresa / Cliente", companyName);
            sheet.Cell(2, ColumnOf(sheet, "Data de início")).Value = new DateTime(2026, 1, 15);

            Write(sheet, 3, "Nome completo", "João Exemplo");
            Write(sheet, 3, "NIF", nifProfileOnly);
            Write(sheet, 3, "Email", "joao.exemplo@example.invalid");
            Write(sheet, 3, "Telefone", "+351900000102");
            Write(sheet, 3, "Morada", "Avenida do Início 3, Porto");
        });

        var preview = await PreviewConsultantsAsync(client, bytes, "Modelo_Importacao_Consultores.xlsx");

        Assert.Equal(2, preview.TotalRows);
        Assert.Equal(2, preview.ValidRows);
        Assert.Equal(0, preview.ErrorRows);
        var withAllocation = preview.Rows.Single(r => r.Nif == nifWithAllocation);
        Assert.Equal("Conceição Fictícia", withAllocation.FullName);
        Assert.Equal(WorkforceType.External, withAllocation.WorkforceType);
        Assert.Equal(EngagementType.ServiceProvider, withAllocation.EngagementType);
        Assert.Equal("Gestão de projeto", withAllocation.RoleOrFunction);
        Assert.Equal(companyName, withAllocation.Company);
        Assert.Equal(new DateOnly(2026, 1, 15), withAllocation.AllocationStartDate);
        Assert.False(preview.Rows.Single(r => r.Nif == nifProfileOnly).HasAllocation);

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/confirm");
        confirmResponse.EnsureSuccessStatusCode();
        var confirmed = (await confirmResponse.Content.ReadFromJsonAsync<ConsultantImportConfirmResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(2, confirmed.CreatedConsultants);
        Assert.Equal(1, confirmed.CreatedAllocations);

        var created = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?nif={nifWithAllocation}", HttpClientTestExtensions.JsonOptions);
        var consultant = Assert.Single(created!.Items);
        Assert.Equal("Conceição Fictícia", consultant.FullName);
        Assert.Equal(WorkforceType.External, consultant.CurrentWorkforceType);
        Assert.Equal(EngagementType.ServiceProvider, consultant.CurrentEngagementType);
        Assert.Equal("Gestão de projeto", consultant.CurrentRoleOrFunction);
        Assert.Equal(companyName, consultant.CurrentCompanyName);
    }

    [Fact]
    public async Task Consultants_LegacyEnglishWorkbook_StillImports()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var nif = NewNif();

        // Sheet name, headers and enum values exactly as the pre-localization template had them.
        var bytes = Phase8TestData.BuildConsultantWorkbook(
            [
                new ConsultantImportRow("Legacy Person", nif, "legacy@example.invalid", "+351900000103", "Rua 103",
                    "External", "EmploymentContract", "Consultant", companyName, new DateOnly(2026, 2, 1), new DateOnly(2026, 12, 31)),
            ],
            sheetName: "Consultants",
            headers: Phase8TestData.LegacyConsultantHeaders);

        var preview = await PreviewConsultantsAsync(client, bytes, "consultant-import-template.xlsx");

        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal(WorkforceType.External, preview.Rows[0].WorkforceType);
        Assert.Equal(EngagementType.EmploymentContract, preview.Rows[0].EngagementType);
        Assert.Equal(new DateOnly(2026, 12, 31), preview.Rows[0].AllocationEndDate);

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/confirm");
        confirmResponse.EnsureSuccessStatusCode();
        var confirmed = (await confirmResponse.Content.ReadFromJsonAsync<ConsultantImportConfirmResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(1, confirmed.CreatedConsultants);
        Assert.Equal(1, confirmed.CreatedAllocations);
    }

    [Fact]
    public async Task Consultants_MixedUntidyHeaders_AndMixedLanguageValues_AreAccepted()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, companyName) = await Phase2TestData.CreateCompanyAsync(client);

        // pt-PT and legacy headers side by side, with stray whitespace and different case.
        string[] headers =
        [
            "  nome COMPLETO ", "nif", "EMAIL", "Phone", "Morada", "WorkforceType", "Tipo  de   vínculo", "FUNÇÃO", "Company",
            "Data de início", "AllocationEndDate",
        ];
        var bytes = Phase8TestData.BuildConsultantWorkbook(
            [
                new ConsultantImportRow("Mixed One", NewNif(), "m1@example.invalid", "+351900000104", "Rua 104",
                    "External", "prestador de SERVIÇO", "Consultant", companyName, new DateOnly(2026, 3, 1)),
                new ConsultantImportRow("Mixed Two", NewNif(), "m2@example.invalid", "+351900000105", "Rua 105",
                    " interno ", "EmploymentContract", "Consultant", null, new DateOnly(2026, 3, 1)),
            ],
            headers: headers);

        var preview = await PreviewConsultantsAsync(client, bytes);

        Assert.Equal(2, preview.ValidRows);
        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal(WorkforceType.External, preview.Rows[0].WorkforceType);
        Assert.Equal(EngagementType.ServiceProvider, preview.Rows[0].EngagementType);
        Assert.Equal(WorkforceType.Internal, preview.Rows[1].WorkforceType);
        Assert.Equal(EngagementType.EmploymentContract, preview.Rows[1].EngagementType);
    }

    [Theory]
    [InlineData("Nome")] // not the header, and not an alias of it
    [InlineData("Nome Completo do Consultor")]
    [InlineData("Full Name")] // the legacy header had no space
    public async Task Consultants_UnrecognizedRequiredHeader_IsRejected_NamingTheMissingColumn(string wrongHeader)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var headers = Phase8TestData.PortugueseConsultantHeaders;
        headers[0] = wrongHeader;
        var bytes = Phase8TestData.BuildConsultantWorkbook(
            [new ConsultantImportRow("Someone", NewNif(), "s@example.invalid", "+351900000106", "Rua 106")], headers: headers);

        var response = await Phase8TestData.PreviewConsultantsAsync(client, bytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var title = await ReadProblemTitleAsync(response);
        Assert.Equal(
            "Faltam colunas obrigatórias no ficheiro: Nome completo. Utilize o modelo de importação sem alterar o nome das colunas.", title);
    }

    [Fact]
    public async Task Consultants_InvalidRows_AreReportedInPortuguese_NamingTheWorkbookHeaders()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var bytes = Phase8TestData.BuildConsultantWorkbook(
            [
                new ConsultantImportRow(string.Empty, NewNif(), "sem.nome@example.invalid", "+351900000107", "Rua 107"),
                new ConsultantImportRow("Valor Inválido", NewNif(), "valor@example.invalid", "+351900000108", "Rua 108",
                    "Banana", "Prestador de serviço", "Consultora", null, new DateOnly(2026, 1, 15)),
                new ConsultantImportRow("Empresa Desconhecida", NewNif(), "empresa@example.invalid", "+351900000109", "Rua 109",
                    "Externo", "Contrato de trabalho", "Consultora", $"Empresa Inexistente {Guid.NewGuid():N}", new DateOnly(2026, 1, 15)),
            ],
            sheetName: "Consultores",
            headers: Phase8TestData.PortugueseConsultantHeaders);

        var preview = await PreviewConsultantsAsync(client, bytes);

        Assert.Equal(3, preview.ErrorRows);

        var missingName = Assert.Single(preview.Rows[0].Issues);
        Assert.Equal(2, preview.Rows[0].RowNumber);
        Assert.Equal("MissingFullName", missingName.Code);
        Assert.Equal("FullName", missingName.Field);
        Assert.Equal("O campo \"Nome completo\" é obrigatório.", missingName.Message);

        var invalidValue = Assert.Single(preview.Rows[1].Issues);
        Assert.Equal("InvalidWorkforceType", invalidValue.Code);
        Assert.Equal("O valor \"Banana\" indicado em \"Regime\" não é válido. Valores aceites: Interno, Externo.", invalidValue.Message);

        var unknownCompany = Assert.Single(preview.Rows[2].Issues);
        Assert.Equal("UnknownCompany", unknownCompany.Code);
        Assert.Equal("Não foi possível encontrar a empresa indicada em \"Empresa / Cliente\".", unknownCompany.Message);
    }

    [Fact]
    public async Task Consultants_DuplicateNif_IsReportedInPortuguese()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, existingNif) = await Phase2TestData.CreateConsultantAsync(client);
        var repeated = NewNif();
        var bytes = Phase8TestData.BuildConsultantWorkbook(
            [
                new ConsultantImportRow("Já Existe", existingNif, "existe@example.invalid", "+351900000110", "Rua 110"),
                new ConsultantImportRow("Repetido Um", repeated, "r1@example.invalid", "+351900000111", "Rua 111"),
                new ConsultantImportRow("Repetido Dois", repeated, "r2@example.invalid", "+351900000112", "Rua 112"),
            ],
            headers: Phase8TestData.PortugueseConsultantHeaders);

        var preview = await PreviewConsultantsAsync(client, bytes);

        Assert.Equal("Já existe um consultor com o NIF indicado.", Assert.Single(preview.Rows[0].Issues).Message);
        Assert.Equal("O valor indicado em \"NIF\" aparece mais do que uma vez no ficheiro.", Assert.Single(preview.Rows[1].Issues).Message);
        Assert.Equal("O valor indicado em \"NIF\" aparece mais do que uma vez no ficheiro.", Assert.Single(preview.Rows[2].Issues).Message);
    }

    [Theory]
    [InlineData("15/01/2026", true)]
    [InlineData("01/15/2026", false)] // US order is never guessed
    [InlineData("15-01-2026", false)]
    [InlineData("2026-01-15", false)]
    [InlineData("15/1/26", false)]
    public async Task Consultants_TextDates_AcceptOnlyDayMonthYear(string text, bool accepted)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var bytes = BuildWorkbook("Consultores", Phase8TestData.PortugueseConsultantHeaders, sheet =>
        {
            Write(sheet, 2, "Nome completo", "Data Em Texto");
            Write(sheet, 2, "NIF", NewNif());
            Write(sheet, 2, "Email", "data@example.invalid");
            Write(sheet, 2, "Telefone", "+351900000113");
            Write(sheet, 2, "Morada", "Rua 113");
            Write(sheet, 2, "Regime", "Interno");
            Write(sheet, 2, "Tipo de vínculo", "Contrato de trabalho");
            Write(sheet, 2, "Função", "Consultora");
            Write(sheet, 2, "Data de início", text);
        });

        var preview = await PreviewConsultantsAsync(client, bytes);

        if (accepted)
        {
            Assert.Equal(1, preview.ValidRows);
            Assert.Equal(new DateOnly(2026, 1, 15), preview.Rows[0].AllocationStartDate);
        }
        else
        {
            var issue = Assert.Single(preview.Rows[0].Issues);
            Assert.Equal("InvalidAllocationDate", issue.Code);
            Assert.Equal("O valor indicado em \"Data de início\" não é uma data válida. Utilize o formato DD/MM/AAAA.", issue.Message);
        }
    }

    [Fact]
    public async Task Consultants_Export_IsInPortuguese_WithTheSameDataAndRealDates()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var nif = NewNif();
        var name = $"Exportação Função {Guid.NewGuid():N}";
        await ImportConsultantsAsync(client,
            new ConsultantImportRow(name, nif, "exp.pt@example.invalid", "+351900000114", "Rua da Série 114",
                "Externo", "Prestador de serviço", "Gestão de atribuição", companyName, new DateOnly(2026, 2, 1), new DateOnly(2026, 11, 30)));

        using var workbook = await ExportAsync(client, $"/api/exports/consultants?search={Uri.EscapeDataString(name)}");

        Assert.Equal(["Consultores", "Alocações"], workbook.Worksheets.Select(s => s.Name));

        var consultants = workbook.Worksheet("Consultores");
        Assert.Equal(ConsultantsSheetHeaders, HeaderRow(consultants));
        var row = Assert.Single(consultants.RowsUsed().Skip(1));
        Assert.Equal(name, row.Cell(2).GetString());
        Assert.Equal(nif, row.Cell(3).GetString());
        Assert.Equal("exp.pt@example.invalid", row.Cell(4).GetString());
        Assert.Equal("+351900000114", row.Cell(5).GetString());
        Assert.Equal("Rua da Série 114", row.Cell(6).GetString());
        Assert.Equal("Sim", row.Cell(7).GetString());
        Assert.Equal("Externo", row.Cell(8).GetString());
        Assert.Equal("Prestador de serviço", row.Cell(9).GetString());
        Assert.Equal("Gestão de atribuição", row.Cell(10).GetString());
        Assert.Equal(companyName, row.Cell(11).GetString());
        Assert.Equal(XLDataType.DateTime, row.Cell(12).DataType);
        Assert.Equal(new DateTime(2026, 2, 1), row.Cell(12).GetDateTime());
        Assert.Equal(new DateTime(2026, 11, 30), row.Cell(13).GetDateTime());
        Assert.Equal("dd/MM/yyyy", row.Cell(12).Style.DateFormat.Format);

        var allocations = workbook.Worksheet("Alocações");
        Assert.Equal(AllocationsSheetHeaders, HeaderRow(allocations));
        var allocation = Assert.Single(allocations.RowsUsed().Skip(1));
        Assert.Equal(name, allocation.Cell(2).GetString());
        Assert.Equal(companyName, allocation.Cell(3).GetString());
        Assert.Equal("Externo", allocation.Cell(4).GetString());
        Assert.Equal("Prestador de serviço", allocation.Cell(5).GetString());
        Assert.Equal(XLDataType.DateTime, allocation.Cell(7).DataType);
        Assert.Equal(new DateTime(2026, 2, 1), allocation.Cell(7).GetDateTime());
        Assert.Equal("Ativa", allocation.Cell(9).GetString());

        AssertNoInternalEnumNames(workbook);
    }

    /// <summary>
    /// Round trip (ADR-070): the exported "Consultores" sheet maps straight
    /// back onto the import's profile columns with no header edited. The rows
    /// are then refused only because those Consultants already exist — the
    /// import creates, it never updates.
    /// </summary>
    [Fact]
    public async Task Consultants_ExportedWorkbook_MapsBackOntoTheImportColumns()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nif = NewNif();
        var name = $"Ida e Volta {Guid.NewGuid():N}";
        await ImportConsultantsAsync(client, new ConsultantImportRow(name, nif, "volta@example.invalid", "+351900000115", "Rua 115"));

        var exportResponse = await client.GetAsync($"/api/exports/consultants?search={Uri.EscapeDataString(name)}");
        exportResponse.EnsureSuccessStatusCode();
        var exported = await exportResponse.Content.ReadAsByteArrayAsync();

        var preview = await PreviewConsultantsAsync(client, exported, "Consultores.xlsx");

        var row = Assert.Single(preview.Rows);
        Assert.Equal(name, row.FullName);
        Assert.Equal(nif, row.Nif);
        Assert.Equal("volta@example.invalid", row.Email);
        Assert.Equal("+351900000115", row.Phone);
        Assert.Equal("Rua 115", row.Address);
        Assert.Equal("DuplicateNifExisting", Assert.Single(row.Issues).Code);
    }

    // -------------------------------------------------------------- Equipment

    [Fact]
    public async Task Equipment_DownloadedPortugueseTemplate_FilledIn_ImportsEquipment()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var generic = await Phase3TestData.CreateEquipmentTypeAsync(client, name: $"Monitor {Guid.NewGuid():N}", kind: "Generic");
        var laptop = await Phase3TestData.CreateEquipmentTypeAsync(client, name: $"Portátil {Guid.NewGuid():N}", kind: "Laptop");
        var (genericSerial, laptopSerial) = (NewSerial(), $"000{NewSerial()}");

        var bytes = await FillTemplateAsync(client, "/api/imports/equipment/template", "Equipamentos", sheet =>
        {
            Write(sheet, 2, "Tipo de equipamento", generic.Name);
            Write(sheet, 2, "Fabricante", "Dell");
            Write(sheet, 2, "Modelo", "U2422H");
            Write(sheet, 2, "Número de série", genericSerial);
            Write(sheet, 2, "Observações", "Monitor de substituição");

            Write(sheet, 3, "Tipo de equipamento", laptop.Name);
            Write(sheet, 3, "Fabricante", "Lenovo");
            Write(sheet, 3, "Modelo", "ThinkPad T14");
            Write(sheet, 3, "Número de série", laptopSerial);
            Write(sheet, 3, "Processador", "Intel Core i5-1345U");
            Write(sheet, 3, "Memória RAM", "16 GB");
            Write(sheet, 3, "Armazenamento", "512 GB SSD");

            Write(sheet, 4, "Tipo de equipamento", generic.Name); // only the type is always required
        });

        var preview = await PreviewEquipmentAsync(client, bytes, "Modelo_Importacao_Equipamentos.xlsx");

        Assert.Equal(3, preview.TotalRows);
        Assert.Equal(3, preview.ValidRows);
        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal("Monitor de substituição", preview.Rows[0].Notes);
        Assert.Equal(laptopSerial, preview.Rows[1].SerialNumber); // leading zeros survive: the column is Text
        Assert.Equal("16 GB", preview.Rows[1].Memory);

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm");
        confirmResponse.EnsureSuccessStatusCode();
        var confirmed = (await confirmResponse.Content.ReadFromJsonAsync<EquipmentImportConfirmResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(3, confirmed.CreatedEquipment);

        var created = await client.GetFromJsonAsync<PagedResponse<EquipmentSummaryResponse>>(
            $"/api/equipment?search={laptopSerial}", HttpClientTestExtensions.JsonOptions);
        var equipment = Assert.Single(created!.Items);
        Assert.Matches("^[A-Z]+-[0-9]{4,}$", equipment.AssetTag); // generated, exactly as before
        Assert.Equal(EquipmentStatus.Available, equipment.Status);
    }

    [Fact]
    public async Task Equipment_LegacyEnglishWorkbook_StillImports()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var laptop = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop");
        var serial = NewSerial();

        var bytes = Phase8TestData.BuildEquipmentWorkbook(
            [new EquipmentImportRow(laptop.Name, "Dell", "Latitude 5440", serial, "Legacy row", "i5-1345U", "16 GB", "512 GB SSD")],
            sheetName: "Equipment",
            headers: Phase8TestData.LegacyEquipmentHeaders);

        var preview = await PreviewEquipmentAsync(client, bytes, "equipment-import-template.xlsx");
        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(0, preview.ErrorRows);

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm");
        confirmResponse.EnsureSuccessStatusCode();
        var confirmed = (await confirmResponse.Content.ReadFromJsonAsync<EquipmentImportConfirmResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(1, confirmed.CreatedEquipment);
    }

    [Fact]
    public async Task Equipment_MixedUntidyHeaders_AreAccepted()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        string[] headers = ["tipo de EQUIPAMENTO", " Manufacturer", "Modelo ", "Número   de série", "NOTES", "Processador", "memória ram", "Storage"];

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, "Logitech", "MX Keys", NewSerial(), "Teclado")], headers: headers);
        var preview = await PreviewEquipmentAsync(client, bytes);

        Assert.Equal(1, preview.ValidRows);
        Assert.Equal("Logitech", preview.Rows[0].Manufacturer);
        Assert.Equal("Teclado", preview.Rows[0].Notes);
    }

    [Theory]
    [InlineData("Número de série", "Numero de serie")] // accents are significant
    [InlineData("Número de série", "N.º de série")]
    [InlineData("Observações", "Notas")]
    [InlineData("Tipo de equipamento", "Tipo")]
    public async Task Equipment_UnrecognizedRequiredHeader_IsRejected_NamingTheMissingColumn(string header, string wrongHeader)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var headers = Phase8TestData.PortugueseEquipmentHeaders;
        headers[Array.IndexOf(headers, header)] = wrongHeader;

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, "Logitech", "MX", NewSerial())], headers: headers);
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            $"Faltam colunas obrigatórias no ficheiro: {header}. Utilize o modelo de importação sem alterar o nome das colunas.",
            await ReadProblemTitleAsync(response));
    }

    [Fact]
    public async Task Equipment_SameColumnTwice_UnderItsPortugueseAndLegacyHeader_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        string[] headers = [.. Phase8TestData.PortugueseEquipmentHeaders, "SerialNumber"];

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, "Logitech", "MX", NewSerial())], headers: headers);
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "O ficheiro tem colunas em duplicado: Número de série. Cada coluna só pode aparecer uma vez.", await ReadProblemTitleAsync(response));
    }

    [Fact]
    public async Task Equipment_InvalidRows_AreReportedInPortuguese_NamingTheWorkbookHeaders()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var laptop = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop");
        var generic = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var existing = await Phase3TestData.CreateGenericEquipmentAsync(client, generic.Id, NewSerial());

        var bytes = Phase8TestData.BuildEquipmentWorkbook(
            [
                new EquipmentImportRow($"Tipo Inexistente {Guid.NewGuid():N}", "Dell", "X", NewSerial()),
                new EquipmentImportRow(laptop.Name, "Dell", "Latitude", null, null, "i5", "16 GB", "512 GB"),
                new EquipmentImportRow(string.Empty, "Dell", "X", NewSerial()),
                new EquipmentImportRow(laptop.Name, "Dell", "Latitude", existing.SerialNumber, null, "i5", "16 GB", "512 GB"),
            ],
            sheetName: "Equipamentos",
            headers: Phase8TestData.PortugueseEquipmentHeaders);

        var preview = await PreviewEquipmentAsync(client, bytes);

        Assert.Equal(4, preview.ErrorRows);

        var unknownType = Assert.Single(preview.Rows[0].Issues);
        Assert.Equal("UnknownEquipmentType", unknownType.Code);
        Assert.Equal("EquipmentType", unknownType.Field);
        Assert.Equal(
            "O valor indicado em \"Tipo de equipamento\" não é válido: não existe nenhum tipo de equipamento com este nome.", unknownType.Message);

        var missingSerial = Assert.Single(preview.Rows[1].Issues);
        Assert.Equal(3, preview.Rows[1].RowNumber);
        Assert.Equal("MissingLaptopSerial", missingSerial.Code);
        Assert.Equal("SerialNumber", missingSerial.Field);
        Assert.Equal("O campo \"Número de série\" é obrigatório para portáteis.", missingSerial.Message);

        Assert.Equal("O campo \"Tipo de equipamento\" é obrigatório.", Assert.Single(preview.Rows[2].Issues).Message);
        Assert.Equal("Já existe um equipamento com o valor indicado em \"Número de série\".", Assert.Single(preview.Rows[3].Issues).Message);
    }

    /// <summary>
    /// A Telemóvel (MobilePhone) row needs an IMEI the workbook has no column
    /// for. It used to pass the preview and then fail at confirmation with a
    /// misleading conflict message; it is now an error from the preview on,
    /// whichever header language or type name the file uses, and the
    /// supported rows next to it are unaffected.
    /// </summary>
    [Theory]
    [InlineData("Telemóvel", "Equipamentos", false)]
    [InlineData("Mobile Phone", "Equipment", true)]
    public async Task Equipment_MobilePhoneRow_IsRejectedAtPreview_AndNeverReachesConfirmation(string typeNamePrefix, string sheetName, bool legacyHeaders)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var phone = await Phase3TestData.CreateEquipmentTypeAsync(client, name: $"{typeNamePrefix} {Guid.NewGuid():N}", kind: "MobilePhone");
        var generic = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var laptop = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop");
        var (phoneSerial, genericSerial, laptopSerial) = (NewSerial(), NewSerial(), NewSerial());

        var bytes = Phase8TestData.BuildEquipmentWorkbook(
            [
                new EquipmentImportRow(generic.Name, "Logitech", "MX Keys", genericSerial),
                new EquipmentImportRow(phone.Name, "Samsung", "Galaxy A05s", phoneSerial),
                new EquipmentImportRow(laptop.Name, "Dell", "Latitude 5440", laptopSerial, null, "i5-1345U", "16 GB", "512 GB SSD"),
            ],
            sheetName: sheetName,
            headers: legacyHeaders ? Phase8TestData.LegacyEquipmentHeaders : Phase8TestData.PortugueseEquipmentHeaders);

        var preview = await PreviewEquipmentAsync(client, bytes);

        Assert.Equal(3, preview.TotalRows);
        Assert.Equal(2, preview.ValidRows);
        Assert.Equal(1, preview.ErrorRows);
        Assert.Equal(ImportRowStatus.Valid, preview.Rows[0].Status);
        Assert.Equal(ImportRowStatus.Valid, preview.Rows[2].Status);

        var phoneRow = preview.Rows[1];
        Assert.Equal(3, phoneRow.RowNumber);
        Assert.Equal(ImportRowStatus.Error, phoneRow.Status);
        var issue = Assert.Single(phoneRow.Issues);
        Assert.Equal("UnsupportedEquipmentKind", issue.Code);
        Assert.Equal("EquipmentType", issue.Field);
        Assert.Equal(
            $"O tipo de equipamento \"{phone.Name}\" não pode ser importado através deste ficheiro. Registe este equipamento diretamente na plataforma.",
            issue.Message);

        // Confirmation refuses the session up front for its row errors — never the misleading conflict/stale message.
        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);
        Assert.Equal("Esta importação ainda tem linhas com erros e não pode ser confirmada.", await ReadProblemTitleAsync(confirmResponse));

        foreach (var serial in new[] { phoneSerial, genericSerial, laptopSerial })
        {
            var found = await client.GetFromJsonAsync<PagedResponse<EquipmentSummaryResponse>>(
                $"/api/equipment?search={serial}", HttpClientTestExtensions.JsonOptions);
            Assert.Empty(found!.Items);
        }
    }

    /// <summary>
    /// Confirmation asks the same question as the preview: a session previewed
    /// before the rule existed (simulated by clearing the row's error in the
    /// stored snapshot) is turned back with the row's own reason, not by
    /// <c>EquipmentService.CreateAsync</c> failing as a "conflict".
    /// </summary>
    [Fact]
    public async Task Equipment_MobilePhoneRow_InASessionPreviewedWithoutTheRule_IsStillRefusedAtConfirmation_WithItsReason()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var phone = await Phase3TestData.CreateEquipmentTypeAsync(client, name: $"Telemóvel {Guid.NewGuid():N}", kind: "MobilePhone");
        var serial = NewSerial();

        var bytes = Phase8TestData.BuildEquipmentWorkbook(
            [new EquipmentImportRow(phone.Name, "Samsung", "Galaxy A05s", serial)],
            sheetName: "Equipamentos",
            headers: Phase8TestData.PortugueseEquipmentHeaders);
        var preview = await PreviewEquipmentAsync(client, bytes);
        Assert.Equal(1, preview.ErrorRows);

        await ClearRowErrorsAsync(preview.ImportSessionId);

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);
        var conflict = (await confirmResponse.Content.ReadFromJsonAsync<EquipmentImportStaleConflictResponse>(HttpClientTestExtensions.JsonOptions))!;
        var issue = Assert.Single(Assert.Single(conflict.Rows).Issues);
        Assert.Equal("UnsupportedEquipmentKind", issue.Code);
        Assert.Equal(
            $"O tipo de equipamento \"{phone.Name}\" não pode ser importado através deste ficheiro. Registe este equipamento diretamente na plataforma.",
            issue.Message);

        var found = await client.GetFromJsonAsync<PagedResponse<EquipmentSummaryResponse>>(
            $"/api/equipment?search={serial}", HttpClientTestExtensions.JsonOptions);
        Assert.Empty(found!.Items);
    }

    /// <summary>Rewrites a stored session as if its preview had found no row errors.</summary>
    private async Task ClearRowErrorsAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await dbContext.ImportSessions.SingleAsync(s => s.Id == sessionId);
        var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var rows = JsonSerializer.Deserialize<List<EquipmentImportRowSnapshot>>(session.RowsJson!, webJson)!;
        session.RowsJson = JsonSerializer.Serialize(rows.Select(r => r with { Status = ImportRowStatus.Valid, Issues = [] }).ToList(), webJson);
        session.ValidRows = session.TotalRows;
        session.ErrorRows = 0;
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Equipment_Export_IsInPortuguese_WithTheSameData()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var generic = await Phase3TestData.CreateEquipmentTypeAsync(client, name: $"Monitor {Guid.NewGuid():N}", kind: "Generic");
        var laptop = await Phase3TestData.CreateEquipmentTypeAsync(client, name: $"Portátil {Guid.NewGuid():N}", kind: "Laptop");
        var (genericSerial, laptopSerial) = (NewSerial(), NewSerial());
        var preview = await PreviewEquipmentAsync(client, Phase8TestData.BuildEquipmentWorkbook(
            [
                new EquipmentImportRow(generic.Name, "Dell", "U2422H", genericSerial),
                new EquipmentImportRow(laptop.Name, "Lenovo", "ThinkPad T14", laptopSerial, null, "Intel Core i5", "16 GB", "512 GB SSD"),
            ],
            headers: Phase8TestData.PortugueseEquipmentHeaders));
        (await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm")).EnsureSuccessStatusCode();

        using var workbook = await ExportAsync(client, "/api/exports/equipment?manufacturer=Lenovo&search=" + laptopSerial);
        var sheet = Assert.Single(workbook.Worksheets);

        Assert.Equal("Equipamentos", sheet.Name);
        Assert.Equal(EquipmentSheetHeaders, HeaderRow(sheet));
        var row = Assert.Single(sheet.RowsUsed().Skip(1));
        Assert.Matches("^[A-Z]+-[0-9]{4,}$", row.Cell(2).GetString());
        Assert.Equal(laptop.Name, row.Cell(3).GetString());
        Assert.Equal("Portátil", row.Cell(4).GetString());
        Assert.Equal("Lenovo", row.Cell(5).GetString());
        Assert.Equal("ThinkPad T14", row.Cell(6).GetString());
        Assert.Equal(laptopSerial, row.Cell(7).GetString());
        Assert.Equal("Disponível", row.Cell(8).GetString());
        Assert.Equal("Intel Core i5", row.Cell(9).GetString());
        Assert.Equal("16 GB", row.Cell(10).GetString());
        Assert.Equal("512 GB SSD", row.Cell(11).GetString());
        AssertNoInternalEnumNames(workbook);

        using var genericWorkbook = await ExportAsync(client, "/api/exports/equipment?search=" + genericSerial);
        Assert.Equal("Genérico", Assert.Single(genericWorkbook.Worksheet("Equipamentos").RowsUsed().Skip(1)).Cell(4).GetString());
    }

    /// <summary>
    /// Round trip (ADR-070): the Equipment export is not an import file, in
    /// either language — it has no "Observações" column, which the import has
    /// always required. Pinned so the contract is explicit rather than implied.
    /// </summary>
    [Fact]
    public async Task Equipment_ExportedWorkbook_IsNotAnImportFile_ItHasNoObservacoesColumn()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var serial = NewSerial();
        var generic = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        await Phase3TestData.CreateGenericEquipmentAsync(client, generic.Id, serial);

        var exportResponse = await client.GetAsync("/api/exports/equipment?search=" + serial);
        exportResponse.EnsureSuccessStatusCode();

        var response = await Phase8TestData.PreviewEquipmentAsync(client, await exportResponse.Content.ReadAsByteArrayAsync(), "Equipamentos.xlsx");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Faltam colunas obrigatórias no ficheiro: Observações. Utilize o modelo de importação sem alterar o nome das colunas.",
            await ReadProblemTitleAsync(response));
    }

    // ---------------------------------------------------------------- Helpers

    private static readonly string[] InternalEnumNames =
    [
        "Internal", "External", "ServiceProvider", "EmploymentContract", "Active", "Ended", "Generic", "Laptop", "MobilePhone", "Available",
        "Assigned", "ReturnPending", "UnderInspection", "Damaged", "UnderMaintenance", "Retired", "TRUE", "FALSE", "True", "False",
    ];

    private static void AssertNoInternalEnumNames(XLWorkbook workbook)
    {
        var values = workbook.Worksheets.SelectMany(s => s.CellsUsed().Select(c => c.GetString())).ToList();
        foreach (var name in InternalEnumNames)
        {
            Assert.DoesNotContain(name, values);
        }
    }

    private static List<string> HeaderRow(IXLWorksheet sheet) => [.. sheet.Row(1).CellsUsed().Select(c => c.GetString())];

    private static int ColumnOf(IXLWorksheet sheet, string header) =>
        sheet.Row(1).CellsUsed().Single(c => c.GetString() == header).Address.ColumnNumber;

    private static void Write(IXLWorksheet sheet, int row, string header, string value) =>
        sheet.Cell(row, ColumnOf(sheet, header)).SetValue(value);

    private static byte[] BuildWorkbook(string sheetName, string[] headers, Action<IXLWorksheet> fill)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).SetValue(headers[i]);
        }

        fill(sheet);
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    /// <summary>Downloads the real template through the API and fills in its data sheet, leaving everything else as generated.</summary>
    private static async Task<byte[]> FillTemplateAsync(HttpClient client, string templateUrl, string sheetName, Action<IXLWorksheet> fill)
    {
        var response = await client.GetAsync(templateUrl);
        response.EnsureSuccessStatusCode();

        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        Assert.Equal(sheetName, workbook.Worksheet(1).Name);
        fill(workbook.Worksheet(1));

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static async Task<ConsultantImportPreviewResponse> PreviewConsultantsAsync(HttpClient client, byte[] bytes, string fileName = "consultores.xlsx")
    {
        var response = await Phase8TestData.PreviewConsultantsAsync(client, bytes, fileName);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<EquipmentImportPreviewResponse> PreviewEquipmentAsync(HttpClient client, byte[] bytes, string fileName = "equipamentos.xlsx")
    {
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes, fileName);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task ImportConsultantsAsync(HttpClient client, params ConsultantImportRow[] rows)
    {
        var preview = await PreviewConsultantsAsync(
            client, Phase8TestData.BuildConsultantWorkbook(rows, sheetName: "Consultores", headers: Phase8TestData.PortugueseConsultantHeaders));
        Assert.Equal(0, preview.ErrorRows);
        (await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/confirm")).EnsureSuccessStatusCode();
    }

    private static async Task<XLWorkbook> ExportAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
    }

    private static async Task<string?> ReadProblemTitleAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("title").GetString();
    }

    private static string NewNif() => Random.Shared.NextInt64(100000000, 999999999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string NewSerial() => $"SN{Guid.NewGuid():N}"[..16];

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
