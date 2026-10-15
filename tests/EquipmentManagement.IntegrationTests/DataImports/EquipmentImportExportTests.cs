using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Api.Contracts.DataImports;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.DataImports;

[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentImportExportTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task DownloadTemplate_ReturnsValidXlsxWithExpectedHeaders()
    {
        using var client = await LoginAsNewAdministratorAsync();

        var response = await client.GetAsync("/api/imports/equipment/template");
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        Assert.Equal("Equipamentos", sheet.Name);
        Assert.Equal("Tipo de equipamento", sheet.Cell(1, 1).GetString());
        Assert.Equal("Número de série", sheet.Cell(1, 4).GetString());
        Assert.True(workbook.Worksheets.Contains("Instruções"));
        Assert.Equal("Modelo_Importacao_Equipamentos.xlsx", response.Content.Headers.ContentDisposition!.FileNameStar);
    }

    [Fact]
    public async Task Preview_ValidGenericRow_IsValid()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var serial = NewSerial();

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, "Logitech", "MX Master", serial)]);
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        response.EnsureSuccessStatusCode();
        var preview = (await response.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(0, preview.ErrorRows);
    }

    [Fact]
    public async Task Preview_ValidLaptopRow_IsValid()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop");
        var serial = NewSerial();

        var bytes = Phase8TestData.BuildEquipmentWorkbook(
            [new EquipmentImportRow(type.Name, "Dell", "Latitude 5440", serial, Processor: "i5-1345U", Memory: "16 GB", Storage: "512 GB SSD")]);
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(1, preview.ValidRows);
    }

    [Fact]
    public async Task Preview_LaptopMissingProcessor_ReportsError()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop");

        var bytes = Phase8TestData.BuildEquipmentWorkbook(
            [new EquipmentImportRow(type.Name, "Dell", "Latitude", NewSerial(), Memory: "16 GB", Storage: "512 GB SSD")]);
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "MissingLaptopProcessor");
    }

    [Fact]
    public async Task Preview_GenericWithLaptopFields_ReportsLaptopFieldsNotAllowed()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, "Dell", Processor: "i5")]);
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "LaptopFieldsNotAllowedForGeneric");
    }

    [Fact]
    public async Task Preview_UnknownEquipmentType_ReportsError()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow("Type That Does Not Exist", SerialNumber: NewSerial())]);
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "UnknownEquipmentType");
    }

    [Fact]
    public async Task Preview_InactiveEquipmentType_ReportsError()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var archiveCsrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync<object?>($"/api/equipment-types/{type.Id}/deactivate", null, archiveCsrf)).EnsureSuccessStatusCode();

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, SerialNumber: NewSerial())]);
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "InactiveEquipmentType");
    }

    [Fact]
    public async Task Preview_DuplicateSerialInFile_ReportsError()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var serial = NewSerial();

        var bytes = Phase8TestData.BuildEquipmentWorkbook(
        [
            new EquipmentImportRow(type.Name, SerialNumber: serial),
            new EquipmentImportRow(type.Name, SerialNumber: serial),
        ]);
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(2, preview.ErrorRows);
        Assert.All(preview.Rows, r => Assert.Contains(r.Issues, i => i.Code == "DuplicateSerialInFile"));
    }

    [Fact]
    public async Task Preview_DuplicateSerialInDatabase_ReportsError()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var serial = NewSerial();
        await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id, serialNumber: serial);

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, SerialNumber: serial)]);
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "DuplicateSerialExisting");
    }

    [Fact]
    public async Task Preview_NumericSerialCell_ReportsNumericIdentifierRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, SerialNumber: "123456789", SerialAsNumber: true)]);
        var response = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "NumericIdentifierRejected");
    }

    [Fact]
    public async Task Confirm_ValidSession_CreatesEquipmentWithGeneratedAssetTagAndLaptopSpec_MarksSessionCompleted_Audits()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop");
        var serial = NewSerial();

        var bytes = Phase8TestData.BuildEquipmentWorkbook(
            [new EquipmentImportRow(type.Name, "Dell", "Latitude 5440", serial, Processor: "i5-1345U", Memory: "16 GB", Storage: "512 GB SSD")]);
        var previewResponse = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm");
        confirmResponse.EnsureSuccessStatusCode();
        var confirmed = (await confirmResponse.Content.ReadFromJsonAsync<EquipmentImportConfirmResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(ImportSessionStatus.Completed, confirmed.Status);
        Assert.Equal(1, confirmed.CreatedEquipment);

        var listResponse = await client.GetFromJsonAsync<PagedResponse<EquipmentSummaryResponse>>(
            $"/api/equipment?search={serial}", HttpClientTestExtensions.JsonOptions);
        var created = Assert.Single(listResponse!.Items);
        Assert.Matches("^[A-Z]+-[0-9]{4,}$", created.AssetTag);
        Assert.Equal(Domain.Equipments.EquipmentStatus.Available, created.Status);

        var detail = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{created.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(detail!.LaptopSpecification);
        Assert.Equal("i5-1345U", detail.LaptopSpecification!.Processor);

        var audit = await GetAuditPageAsync(client, preview.ImportSessionId, "EquipmentImport.Completed");
        var entry = Assert.Single(audit.Items);
        var combined = $"{entry.OldValuesJson}{entry.NewValuesJson}{entry.MetadataJson}";
        Assert.DoesNotContain(serial, combined);
    }

    [Fact]
    public async Task Confirm_StalePreview_SerialTakenSincePreview_RollsBackEverything()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var serial = NewSerial();

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, SerialNumber: serial)]);
        var previewResponse = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(0, preview.ErrorRows);

        // Someone else registers equipment with the same serial between preview and confirm.
        await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id, serialNumber: serial);

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

        var conflict = (await confirmResponse.Content.ReadFromJsonAsync<EquipmentImportStaleConflictResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Contains(conflict.Rows, r => r.Issues.Any(i => i.Code == "DuplicateSerialExisting"));
    }

    [Fact]
    public async Task Confirm_ConcurrentDoubleConfirm_OnlyOneSucceeds()
    {
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(clientA, kind: "Generic");
        var serial = NewSerial();

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, SerialNumber: serial)]);
        var previewResponse = await Phase8TestData.PreviewEquipmentAsync(clientA, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        var results = await Task.WhenAll(
            Phase8TestData.ConfirmAsync(clientA, $"/api/imports/equipment/{preview.ImportSessionId}/confirm"),
            Phase8TestData.ConfirmAsync(clientB, $"/api/imports/equipment/{preview.ImportSessionId}/confirm"));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);

        var listResponse = await clientA.GetFromJsonAsync<PagedResponse<EquipmentSummaryResponse>>(
            $"/api/equipment?search={serial}", HttpClientTestExtensions.JsonOptions);
        Assert.Single(listResponse!.Items);
    }

    [Fact]
    public async Task Confirm_CompletedSession_NoLongerRetainsRowPii()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var serial = NewSerial();

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, "Logitech", "MX Master", serial)]);
        var previewResponse = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Single(preview.Rows);

        (await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm")).EnsureSuccessStatusCode();

        var sessionResponse = await client.GetFromJsonAsync<EquipmentImportPreviewResponse>(
            $"/api/imports/equipment/{preview.ImportSessionId}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ImportSessionStatus.Completed, sessionResponse!.Status);
        Assert.Equal(1, sessionResponse.TotalRows);
        Assert.Empty(sessionResponse.Rows);
    }

    [Fact]
    public async Task Confirm_ExpiredPendingSession_ReturnsImportSessionExpired_ClearsPii_AndImportsNothing()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var serial = NewSerial();

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, SerialNumber: serial)]);
        var previewResponse = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(0, preview.ErrorRows);

        await BackdateExpiryAsync(preview.ImportSessionId);

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

        var sessionResponse = await client.GetFromJsonAsync<EquipmentImportPreviewResponse>(
            $"/api/imports/equipment/{preview.ImportSessionId}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ImportSessionStatus.Pending, sessionResponse!.Status);
        Assert.Empty(sessionResponse.Rows);

        var listResponse = await client.GetFromJsonAsync<PagedResponse<EquipmentSummaryResponse>>(
            $"/api/equipment?search={serial}", HttpClientTestExtensions.JsonOptions);
        Assert.Empty(listResponse!.Items);
    }

    [Fact]
    public async Task Confirm_BeforeExpiry_StillSucceeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, SerialNumber: NewSerial())]);
        var previewResponse = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm");
        confirmResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Cancel_PendingSession_ClearsPiiImmediately_KeepsMetadata_Audits()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var serial = NewSerial();

        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, SerialNumber: serial)]);
        var previewResponse = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        var cancelResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/cancel");
        cancelResponse.EnsureSuccessStatusCode();
        var cancelled = (await cancelResponse.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(ImportSessionStatus.Cancelled, cancelled.Status);
        Assert.Equal(1, cancelled.TotalRows);
        Assert.Empty(cancelled.Rows);

        var sessionResponse = await client.GetFromJsonAsync<EquipmentImportPreviewResponse>(
            $"/api/imports/equipment/{preview.ImportSessionId}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ImportSessionStatus.Cancelled, sessionResponse!.Status);
        Assert.Empty(sessionResponse.Rows);

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

        var listResponse = await client.GetFromJsonAsync<PagedResponse<EquipmentSummaryResponse>>(
            $"/api/equipment?search={serial}", HttpClientTestExtensions.JsonOptions);
        Assert.Empty(listResponse!.Items);

        var audit = await GetAuditPageAsync(client, preview.ImportSessionId, "EquipmentImport.Cancelled");
        var entry = Assert.Single(audit.Items);
        var combined = $"{entry.OldValuesJson}{entry.NewValuesJson}{entry.MetadataJson}";
        Assert.DoesNotContain(serial, combined);
    }

    /// <summary>Simulates the passage of the configured validity window without waiting for it or introducing a fake clock, by directly backdating the already-persisted ExpiresAtUtc.</summary>
    private async Task BackdateExpiryAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await dbContext.ImportSessions.SingleAsync(s => s.Id == sessionId);
        session.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Preview_RequiresCsrfToken()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow("X", SerialNumber: NewSerial())]);

        using var content = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "equipment.xlsx" } };
        var response = await client.PostAsync("/api/imports/equipment/preview", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hr_CanPreviewConfirmAndExport_SameAsAdministration()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR Equip Import, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, SerialNumber: NewSerial())]);
        var previewResponse = await Phase8TestData.PreviewEquipmentAsync(hrClient, bytes);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        var confirmResponse = await Phase8TestData.ConfirmAsync(hrClient, $"/api/imports/equipment/{preview.ImportSessionId}/confirm");
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var exportResponse = await hrClient.GetAsync("/api/exports/equipment");
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsValidWorkbook_WithCustodyOnlyFromActiveAssignment_AndAudits()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var serial = NewSerial();
        var bytes = Phase8TestData.BuildEquipmentWorkbook([new EquipmentImportRow(type.Name, "Logitech", "MX Master", serial)]);
        var previewResponse = await Phase8TestData.PreviewEquipmentAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<EquipmentImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
        (await Phase8TestData.ConfirmAsync(client, $"/api/imports/equipment/{preview.ImportSessionId}/confirm")).EnsureSuccessStatusCode();

        var exportResponse = await client.GetAsync("/api/exports/equipment");
        exportResponse.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", exportResponse.Content.Headers.ContentType!.MediaType);

        var exportedBytes = await exportResponse.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(exportedBytes));
        Assert.Matches(@"^Equipamentos_\d{4}-\d{2}-\d{2}\.xlsx$", exportResponse.Content.Headers.ContentDisposition!.FileNameStar);
        var sheet = workbook.Worksheet("Equipamentos");

        var row = sheet.RowsUsed().Skip(1).FirstOrDefault(r => r.Cell(7).GetString() == serial);
        Assert.NotNull(row);
        Assert.Equal("Disponível", row!.Cell(8).GetString());
        Assert.Empty(row.Cell(12).GetString()); // "Consultor atual" blank — no active assignment.

        var audit = await GetAuditPageAsync(client, null, "EquipmentExport.Generated");
        Assert.NotEmpty(audit.Items);
    }

    private static string NewSerial() => $"SN{Guid.NewGuid():N}"[..16];

    private static async Task<PagedResponse<AuditLogResponse>> GetAuditPageAsync(HttpClient client, Guid? entityId, string action)
    {
        var url = entityId is { } id ? $"/api/audit?entityId={id}&action={action}" : $"/api/audit?action={action}";
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions))!;
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
