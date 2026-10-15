using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Api.Contracts.Companies;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.DataImports;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.DataImports;

[Collection(Phase1CollectionDefinition.Name)]
public sealed class ConsultantImportExportTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task DownloadTemplate_ReturnsValidXlsxWithExpectedHeaders()
    {
        using var client = await LoginAsNewAdministratorAsync();

        var response = await client.GetAsync("/api/imports/consultants/template");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        Assert.Equal("Consultores", sheet.Name);
        Assert.Equal("Nome completo", sheet.Cell(1, 1).GetString());
        Assert.Equal("NIF", sheet.Cell(1, 2).GetString());
        Assert.True(workbook.Worksheets.Contains("Instruções"));
        Assert.Equal("Modelo_Importacao_Consultores.xlsx", response.Content.Headers.ContentDisposition!.FileNameStar);
    }

    [Fact]
    public async Task Preview_ValidFile_AllRowsValid()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nif = NewNif();
        var bytes = Phase8TestData.BuildConsultantWorkbook([new ConsultantImportRow("Maria Exemplo", nif, "maria@example.invalid", "+351900000000", "Rua 1")]);

        var response = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        response.EnsureSuccessStatusCode();
        var preview = (await response.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(1, preview.TotalRows);
        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal(ImportSessionStatus.Pending, preview.Status);
        Assert.Equal(ImportRowStatus.Valid, preview.Rows[0].Status);
    }

    [Fact]
    public async Task Preview_DuplicateNifInFile_ReportsErrorOnBothRows()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nif = NewNif();
        var bytes = Phase8TestData.BuildConsultantWorkbook(
        [
            new ConsultantImportRow("Row A", nif, "a@example.invalid", "+351900000001", "Rua 1"),
            new ConsultantImportRow("Row B", nif, "b@example.invalid", "+351900000002", "Rua 2"),
        ]);

        var response = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(2, preview.ErrorRows);
        Assert.All(preview.Rows, r => Assert.Contains(r.Issues, i => i.Code == "DuplicateNifInFile"));
    }

    [Fact]
    public async Task Preview_NifAlreadyInDatabase_ReportsDuplicateNifExisting()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, existingNif) = await Phase2TestData.CreateConsultantAsync(client);

        var bytes = Phase8TestData.BuildConsultantWorkbook([new ConsultantImportRow("New Person", existingNif, "n@example.invalid", "+351900000003", "Rua 3")]);
        var response = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(1, preview.ErrorRows);
        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "DuplicateNifExisting");
    }

    [Fact]
    public async Task Preview_UnknownCompany_ReportsError()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var bytes = Phase8TestData.BuildConsultantWorkbook(
        [
            new ConsultantImportRow("Ext Person", NewNif(), "e@example.invalid", "+351900000004", "Rua 4",
                "External", "ServiceProvider", "Consultant", "Company That Does Not Exist", new DateOnly(2026, 1, 15)),
        ]);

        var response = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(1, preview.ErrorRows);
        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "UnknownCompany");
    }

    [Fact]
    public async Task Preview_InactiveCompany_ReportsError()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (companyId, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var deactivateCsrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync<object?>($"/api/companies/{companyId}/deactivate", null, deactivateCsrf)).EnsureSuccessStatusCode();

        var bytes = Phase8TestData.BuildConsultantWorkbook(
        [
            new ConsultantImportRow("Ext Person", NewNif(), "e2@example.invalid", "+351900000005", "Rua 5",
                "External", "ServiceProvider", "Consultant", companyName, new DateOnly(2026, 1, 15)),
        ]);

        var response = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "InactiveCompany");
    }

    [Fact]
    public async Task Preview_InternalWithCompany_ReportsCompanyNotAllowed()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, companyName) = await Phase2TestData.CreateCompanyAsync(client);

        var bytes = Phase8TestData.BuildConsultantWorkbook(
        [
            new ConsultantImportRow("Int Person", NewNif(), "i@example.invalid", "+351900000006", "Rua 6",
                "Internal", "EmploymentContract", "Consultant", companyName, new DateOnly(2026, 1, 15)),
        ]);

        var response = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "CompanyNotAllowedForInternal");
    }

    [Fact]
    public async Task Preview_FormulaCell_ReportsFormulaNotAllowed()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var bytes = Phase8TestData.BuildConsultantWorkbook(
            [new ConsultantImportRow("X", NewNif(), "x@example.invalid", "+351900000007", "Rua 7", FullNameAsFormula: true)]);

        var response = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "FormulaNotAllowed" && i.Field == "FullName");
    }

    [Fact]
    public async Task Preview_NumericNifCell_ReportsNumericIdentifierRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var bytes = Phase8TestData.BuildConsultantWorkbook(
            [new ConsultantImportRow("X", "123456789", "x2@example.invalid", "+351900000008", "Rua 8", NifAsNumber: true)]);

        var response = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await response.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "NumericIdentifierRejected");
    }

    [Fact]
    public async Task Confirm_ValidSession_CreatesConsultantAndAllocation_MarksSessionCompleted_Audits()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var nif = NewNif();

        var bytes = Phase8TestData.BuildConsultantWorkbook(
        [
            new ConsultantImportRow("Confirmed Person", nif, "c@example.invalid", "+351900000009", "Rua 9",
                "External", "ServiceProvider", "Consultant", companyName, new DateOnly(2026, 1, 15)),
        ]);

        var previewResponse = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/confirm");
        confirmResponse.EnsureSuccessStatusCode();
        var confirmed = (await confirmResponse.Content.ReadFromJsonAsync<ConsultantImportConfirmResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(ImportSessionStatus.Completed, confirmed.Status);
        Assert.Equal(1, confirmed.CreatedConsultants);
        Assert.Equal(1, confirmed.CreatedAllocations);

        var listResponse = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?nif={nif}", HttpClientTestExtensions.JsonOptions);
        Assert.Single(listResponse!.Items);
        Assert.Equal("Confirmed Person", listResponse.Items[0].FullName);

        var sessionResponse = await client.GetFromJsonAsync<ConsultantImportPreviewResponse>(
            $"/api/imports/consultants/{preview.ImportSessionId}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ImportSessionStatus.Completed, sessionResponse!.Status);

        var audit = await GetAuditPageAsync(client, preview.ImportSessionId, "ConsultantImport.Completed");
        var entry = Assert.Single(audit.Items);
        var combined = $"{entry.OldValuesJson}{entry.NewValuesJson}{entry.MetadataJson}";
        Assert.DoesNotContain(nif, combined);
        Assert.DoesNotContain("Confirmed Person", combined);
    }

    [Fact]
    public async Task Confirm_SessionWithErrorRows_ReturnsConflict()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nif = NewNif();
        var bytes = Phase8TestData.BuildConsultantWorkbook(
        [
            new ConsultantImportRow("A", nif, "a3@example.invalid", "+351900000010", "Rua 10"),
            new ConsultantImportRow("B", nif, "b3@example.invalid", "+351900000011", "Rua 11"),
        ]);

        var previewResponse = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.True(preview.ErrorRows > 0);

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

        var listResponse = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?nif={nif}", HttpClientTestExtensions.JsonOptions);
        Assert.Empty(listResponse!.Items);
    }

    [Fact]
    public async Task Confirm_StalePreview_DatabaseChangedSinceLastPreview_RollsBackEverything()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nif = NewNif();
        var otherNif = NewNif();
        var bytes = Phase8TestData.BuildConsultantWorkbook(
        [
            new ConsultantImportRow("Stale A", nif, "sa@example.invalid", "+351900000012", "Rua 12"),
            new ConsultantImportRow("Stale B", otherNif, "sb@example.invalid", "+351900000013", "Rua 13"),
        ]);

        var previewResponse = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(0, preview.ErrorRows);

        // Someone else creates a Consultant with the same NIF as row 1, between preview and confirm.
        var createCsrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync("/api/consultants", new
        {
            FullName = "Someone Else",
            Nif = nif,
            Email = $"other.{Guid.NewGuid():N}@example.invalid",
            Address = "Other Address",
            Phone = "+351900000099",
        }, createCsrf)).EnsureSuccessStatusCode();

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

        var conflict = (await confirmResponse.Content.ReadFromJsonAsync<ConsultantImportStaleConflictResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Contains(conflict.Rows, r => r.Issues.Any(i => i.Code == "DuplicateNifExisting"));

        // Nothing from THIS import session was persisted — "Stale B" (row 2) must not exist, even though only row 1 conflicted.
        var listResponse = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?nif={otherNif}", HttpClientTestExtensions.JsonOptions);
        Assert.Empty(listResponse!.Items);
    }

    [Fact]
    public async Task Confirm_ConcurrentDoubleConfirm_OnlyOneSucceeds()
    {
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();

        var nif = NewNif();
        var bytes = Phase8TestData.BuildConsultantWorkbook([new ConsultantImportRow("Concurrent Person", nif, "cc@example.invalid", "+351900000014", "Rua 14")]);
        var previewResponse = await Phase8TestData.PreviewConsultantsAsync(clientA, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        var results = await Task.WhenAll(
            Phase8TestData.ConfirmAsync(clientA, $"/api/imports/consultants/{preview.ImportSessionId}/confirm"),
            Phase8TestData.ConfirmAsync(clientB, $"/api/imports/consultants/{preview.ImportSessionId}/confirm"));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);

        // Scoped by the unique NIF generated for this test run — a plain
        // name-based search would accumulate matches from other runs against
        // this shared, never-truncated dev database (see README's "Known
        // limitation" on this).
        var listResponse = await clientA.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?nif={nif}", HttpClientTestExtensions.JsonOptions);
        Assert.Single(listResponse!.Items);
    }

    [Fact]
    public async Task Confirm_CompletedSession_NoLongerRetainsRowPii()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nif = NewNif();
        var bytes = Phase8TestData.BuildConsultantWorkbook([new ConsultantImportRow("Retention Person", nif, "ret@example.invalid", "+351900000020", "Rua 20")]);

        var previewResponse = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Single(preview.Rows);

        (await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/confirm")).EnsureSuccessStatusCode();

        // Non-PII summary counts (Phase 8 retention review requirement) survive completion...
        var sessionResponse = await client.GetFromJsonAsync<ConsultantImportPreviewResponse>(
            $"/api/imports/consultants/{preview.ImportSessionId}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ImportSessionStatus.Completed, sessionResponse!.Status);
        Assert.Equal(1, sessionResponse.TotalRows);
        Assert.Equal(1, sessionResponse.ValidRows);

        // ...but the detailed row snapshot (name/NIF/email/phone/address) does not.
        Assert.Empty(sessionResponse.Rows);
    }

    [Fact]
    public async Task Confirm_ExpiredPendingSession_ReturnsImportSessionExpired_ClearsPii_AndImportsNothing()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nif = NewNif();
        var bytes = Phase8TestData.BuildConsultantWorkbook([new ConsultantImportRow("Expired Person", nif, "exp2@example.invalid", "+351900000021", "Rua 21")]);

        var previewResponse = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(0, preview.ErrorRows);

        await BackdateExpiryAsync(preview.ImportSessionId);

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

        // Still Pending (never silently completed/revived) but its PII snapshot is now gone.
        var sessionResponse = await client.GetFromJsonAsync<ConsultantImportPreviewResponse>(
            $"/api/imports/consultants/{preview.ImportSessionId}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ImportSessionStatus.Pending, sessionResponse!.Status);
        Assert.Empty(sessionResponse.Rows);

        var listResponse = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?nif={nif}", HttpClientTestExtensions.JsonOptions);
        Assert.Empty(listResponse!.Items);
    }

    [Fact]
    public async Task Confirm_BeforeExpiry_StillSucceeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nif = NewNif();
        var bytes = Phase8TestData.BuildConsultantWorkbook([new ConsultantImportRow("Not Yet Expired", nif, "nye@example.invalid", "+351900000022", "Rua 22")]);

        var previewResponse = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/confirm");
        confirmResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Cancel_PendingSession_ClearsPiiImmediately_KeepsMetadata_Audits()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nif = NewNif();
        var bytes = Phase8TestData.BuildConsultantWorkbook([new ConsultantImportRow("Cancel Person", nif, "cancel@example.invalid", "+351900000023", "Rua 23")]);

        var previewResponse = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        var cancelResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/cancel");
        cancelResponse.EnsureSuccessStatusCode();
        var cancelled = (await cancelResponse.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(ImportSessionStatus.Cancelled, cancelled.Status);
        Assert.Equal(1, cancelled.TotalRows);
        Assert.Empty(cancelled.Rows);

        var sessionResponse = await client.GetFromJsonAsync<ConsultantImportPreviewResponse>(
            $"/api/imports/consultants/{preview.ImportSessionId}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ImportSessionStatus.Cancelled, sessionResponse!.Status);
        Assert.Empty(sessionResponse.Rows);

        // A cancelled session can never later be confirmed.
        var confirmResponse = await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/confirm");
        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

        var listResponse = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?nif={nif}", HttpClientTestExtensions.JsonOptions);
        Assert.Empty(listResponse!.Items);

        var audit = await GetAuditPageAsync(client, preview.ImportSessionId, "ConsultantImport.Cancelled");
        var entry = Assert.Single(audit.Items);
        var combined = $"{entry.OldValuesJson}{entry.NewValuesJson}{entry.MetadataJson}";
        Assert.DoesNotContain(nif, combined);
        Assert.DoesNotContain("Cancel Person", combined);
    }

    /// <summary>
    /// Simulates the passage of the configured validity window without waiting for it
    /// or introducing a fake clock: directly backdates the already-persisted
    /// <see cref="ImportSession.ExpiresAtUtc"/> the same way real elapsed time would.
    /// </summary>
    private async Task BackdateExpiryAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await dbContext.ImportSessions.SingleAsync(s => s.Id == sessionId);
        session.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Preview_RequiresAuthentication()
    {
        using var client = factory.CreateClient();
        var bytes = Phase8TestData.BuildConsultantWorkbook([new ConsultantImportRow("X", NewNif(), "x3@example.invalid", "+351900000015", "Rua 15")]);

        using var content = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "consultants.xlsx" } };
        var response = await client.PostAsync("/api/imports/consultants/preview", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Preview_RequiresCsrfToken()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var bytes = Phase8TestData.BuildConsultantWorkbook([new ConsultantImportRow("X", NewNif(), "x4@example.invalid", "+351900000016", "Rua 16")]);

        using var content = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "consultants.xlsx" } };
        var response = await client.PostAsync("/api/imports/consultants/preview", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hr_CanPreviewConfirmAndExport_SameAsAdministration()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR Import, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var bytes = Phase8TestData.BuildConsultantWorkbook([new ConsultantImportRow("HR Imported", NewNif(), "hr@example.invalid", "+351900000017", "Rua 17")]);
        var previewResponse = await Phase8TestData.PreviewConsultantsAsync(hrClient, bytes);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;

        var confirmResponse = await Phase8TestData.ConfirmAsync(hrClient, $"/api/imports/consultants/{preview.ImportSessionId}/confirm");
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var exportResponse = await hrClient.GetAsync("/api/exports/consultants");
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsValidWorkbookWithConsultantsAndAllocationsSheets_AndAudits()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var nif = NewNif();
        var bytes = Phase8TestData.BuildConsultantWorkbook(
        [
            new ConsultantImportRow("Export Person", nif, "exp@example.invalid", "+351900000018", "Rua 18",
                "External", "ServiceProvider", "Consultant", companyName, new DateOnly(2026, 2, 1)),
        ]);
        var previewResponse = await Phase8TestData.PreviewConsultantsAsync(client, bytes);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ConsultantImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
        (await Phase8TestData.ConfirmAsync(client, $"/api/imports/consultants/{preview.ImportSessionId}/confirm")).EnsureSuccessStatusCode();

        var exportResponse = await client.GetAsync("/api/exports/consultants");
        exportResponse.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", exportResponse.Content.Headers.ContentType!.MediaType);
        Assert.Matches(@"^Consultores_\d{4}-\d{2}-\d{2}\.xlsx$", exportResponse.Content.Headers.ContentDisposition!.FileNameStar);

        var exportedBytes = await exportResponse.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(exportedBytes));
        Assert.True(workbook.Worksheets.Contains("Consultores"));
        Assert.True(workbook.Worksheets.Contains("Alocações"));

        var consultantsSheet = workbook.Worksheet("Consultores");
        var nifColumnValues = consultantsSheet.RowsUsed().Skip(1).Select(r => r.Cell(3).GetString());
        Assert.Contains(nif, nifColumnValues);

        var audit = await GetAuditPageAsync(client, null, "ConsultantExport.Generated");
        Assert.NotEmpty(audit.Items);
    }

    private static string NewNif() => Random.Shared.NextInt64(100000000, 999999999).ToString(System.Globalization.CultureInfo.InvariantCulture);

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
