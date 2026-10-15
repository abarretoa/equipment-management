using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.ResponsibilityTerms;

/// <summary>
/// The Phase 7 externally-signed-document workflow against a real PostgreSQL
/// database and private file storage (Phase 7 scope,
/// 63, 70-73).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class ResponsibilityTermSignedDocumentTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Upload_ValidPdf_AcceptedAndBytePreservedExactly_StorageKeyNeverExposed()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);
        var signedOn = new DateOnly(2026, 9, 13);

        var uploadResponse = await Phase7TestData.UploadSignedDocumentAsync(client, term.Id, signedOn: signedOn);
        uploadResponse.EnsureSuccessStatusCode();
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storageKey", uploadBody, StringComparison.OrdinalIgnoreCase);

        var signature = System.Text.Json.JsonSerializer.Deserialize<ResponsibilityTermSignatureResponse>(uploadBody, HttpClientTestExtensions.JsonOptions)!;
        Assert.Equal(signedOn, signature.SignedOn);
        Assert.Equal(Phase7TestData.MinimalPdfBytes.LongLength, signature.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Phase7TestData.MinimalPdfBytes)), signature.Sha256);

        var contentResponse = await client.GetAsync($"/api/responsibility-terms/{term.Id}/signed-document/content");
        contentResponse.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", contentResponse.Content.Headers.ContentType!.MediaType);
        var downloadedBytes = await contentResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(Phase7TestData.MinimalPdfBytes, downloadedBytes);

        var dispositionValue = contentResponse.Content.Headers.ContentDisposition!.ToString();
        Assert.Contains(term.DocumentNumber, dispositionValue);
        Assert.Contains("signed", dispositionValue, StringComparison.OrdinalIgnoreCase);

        var detail = await client.GetFromJsonAsync<ResponsibilityTermResponse>($"/api/responsibility-terms/{term.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ResponsibilityTermExecutionStatus.Signed, detail!.ExecutionStatus);
        Assert.NotNull(detail.Signature);
        Assert.Equal(signedOn, detail.Signature!.SignedOn);
    }

    [Fact]
    public async Task Upload_DeclaredNonPdfContentType_Rejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        var response = await Phase7TestData.UploadSignedDocumentAsync(client, term.Id, contentType: "image/png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_FakePdfBytesDeclaredAsPdf_Rejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        var fakeBytes = "this is not a real pdf, just renamed"u8.ToArray();
        var response = await Phase7TestData.UploadSignedDocumentAsync(client, term.Id, bytes: fakeBytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_OversizedFile_Rejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        // Bigger than the 15 MB default MaxSignedDocumentSizeBytes but still
        // under the 20 MB Kestrel RequestSizeLimit on the endpoint, so this
        // exercises the domain-level size check, not the transport limit.
        var padding = new byte[16 * 1024 * 1024];
        Array.Fill(padding, (byte)'x');
        var oversized = "%PDF-1.7\n"u8.ToArray().Concat(padding).Concat("\n%%EOF"u8.ToArray()).ToArray();

        var response = await Phase7TestData.UploadSignedDocumentAsync(client, term.Id, bytes: oversized);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ForNonexistentTerm_ReturnsNotFound()
    {
        using var client = await LoginAsNewAdministratorAsync();

        var response = await Phase7TestData.UploadSignedDocumentAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_SecondUploadForSameTerm_Rejected_NoOverwrite()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        (await Phase7TestData.UploadSignedDocumentAsync(client, term.Id)).EnsureSuccessStatusCode();
        var secondBytes = "%PDF-1.7\ndifferent content\n%%EOF"u8.ToArray();
        var secondResponse = await Phase7TestData.UploadSignedDocumentAsync(client, term.Id, bytes: secondBytes);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        // The original signed artifact must remain exactly what was first uploaded.
        var contentResponse = await client.GetAsync($"/api/responsibility-terms/{term.Id}/signed-document/content");
        contentResponse.EnsureSuccessStatusCode();
        var bytes = await contentResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(Phase7TestData.MinimalPdfBytes, bytes);
    }

    [Fact]
    public async Task Upload_ConcurrentForSameTerm_OnlyOneSucceeds()
    {
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(clientA);
        var term = await GenerateTermAsync(clientA, handover.Id);

        var results = await Task.WhenAll(
            Phase7TestData.UploadSignedDocumentAsync(clientA, term.Id),
            Phase7TestData.UploadSignedDocumentAsync(clientB, term.Id));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Upload_RequiresAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/responsibility-terms/{Guid.NewGuid()}/signed-document",
            new MultipartFormDataContent { { new ByteArrayContent(Phase7TestData.MinimalPdfBytes), "file", "signed.pdf" } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_RequiresCsrfToken()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(Phase7TestData.MinimalPdfBytes), "file", "signed.pdf" },
        };
        var response = await client.PostAsync($"/api/responsibility-terms/{term.Id}/signed-document", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hr_CanUploadAndDownloadSignedDocument_SameAsAdministration()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR Signed Doc, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var handover = await CreateFinalizedHandoverAsync(hrClient);
        var term = await GenerateTermAsync(hrClient, handover.Id);

        var uploadResponse = await Phase7TestData.UploadSignedDocumentAsync(hrClient, term.Id);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

        var downloadResponse = await hrClient.GetAsync($"/api/responsibility-terms/{term.Id}/signed-document/content");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
    }

    [Fact]
    public async Task Upload_RecordsAuditEvent_AndDownloadIsAudited_NeitherLeaksStorageKey()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var term = await GenerateTermAsync(client, handover.Id);

        (await Phase7TestData.UploadSignedDocumentAsync(client, term.Id)).EnsureSuccessStatusCode();

        var uploadAudit = await GetAuditPageAsync(client, term.Id, "ResponsibilityTerm.SignedDocumentUploaded");
        var uploadEntry = Assert.Single(uploadAudit.Items);
        var uploadCombined = $"{uploadEntry.OldValuesJson}{uploadEntry.NewValuesJson}{uploadEntry.MetadataJson}";
        Assert.DoesNotContain("storageKey", uploadCombined, StringComparison.OrdinalIgnoreCase);

        (await client.GetAsync($"/api/responsibility-terms/{term.Id}/signed-document/content")).EnsureSuccessStatusCode();

        var downloadAudit = await GetAuditPageAsync(client, term.Id, "ResponsibilityTerm.SignedDocumentDownloaded");
        var downloadEntry = Assert.Single(downloadAudit.Items);
        var downloadCombined = $"{downloadEntry.OldValuesJson}{downloadEntry.NewValuesJson}{downloadEntry.MetadataJson}";
        Assert.DoesNotContain("storageKey", downloadCombined, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Phase 7 scope: v1 generated, sent, and signed; v2 is then
    /// generated before v1's signature is superseded. v1 must remain
    /// Superseded AND Signed; v2 must be Generated AND NotSent — the
    /// signature must never transfer to v2.
    /// </summary>
    [Fact]
    public async Task SignedBeforeRegeneration_V1RemainsSupersededAndSigned_V2IsGeneratedAndUnsigned()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var v1 = await GenerateTermAsync(client, handover.Id);

        (await Phase7TestData.SendAsync(client, v1.Id)).EnsureSuccessStatusCode();
        (await Phase7TestData.UploadSignedDocumentAsync(client, v1.Id)).EnsureSuccessStatusCode();

        var v2 = await GenerateTermAsync(client, handover.Id);

        var v1Detail = await client.GetFromJsonAsync<ResponsibilityTermResponse>($"/api/responsibility-terms/{v1.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ResponsibilityTermStatus.Superseded, v1Detail!.Status);
        Assert.Equal(ResponsibilityTermExecutionStatus.Signed, v1Detail.ExecutionStatus);
        Assert.NotNull(v1Detail.Signature);

        var v2Detail = await client.GetFromJsonAsync<ResponsibilityTermResponse>($"/api/responsibility-terms/{v2.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ResponsibilityTermStatus.Generated, v2Detail!.Status);
        Assert.Equal(ResponsibilityTermExecutionStatus.NotSent, v2Detail.ExecutionStatus);
        Assert.Null(v2Detail.Signature);

        // v2 cannot be sent using v1's already-uploaded signature — it has none of its own yet.
        var v2ContentResponse = await client.GetAsync($"/api/responsibility-terms/{v2.Id}/signed-document/content");
        Assert.Equal(HttpStatusCode.NotFound, v2ContentResponse.StatusCode);
    }

    /// <summary>
    /// Phase 7 scope: v1 sent, v2 generated before the consultant
    /// returns v1 signed. The late signed v1 must still be truthfully
    /// recordable against v1 itself — never silently rejected or reattached
    /// to v2.
    /// </summary>
    [Fact]
    public async Task LateSignedReturnForSupersededTerm_IsAcceptedAgainstThatExactVersion()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);
        var v1 = await GenerateTermAsync(client, handover.Id);

        (await Phase7TestData.SendAsync(client, v1.Id)).EnsureSuccessStatusCode();
        var v2 = await GenerateTermAsync(client, handover.Id);

        // The consultant returns the signed v1 only now, after v2 already exists.
        var lateUploadResponse = await Phase7TestData.UploadSignedDocumentAsync(client, v1.Id);
        lateUploadResponse.EnsureSuccessStatusCode();

        var v1Detail = await client.GetFromJsonAsync<ResponsibilityTermResponse>($"/api/responsibility-terms/{v1.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ResponsibilityTermStatus.Superseded, v1Detail!.Status);
        Assert.Equal(ResponsibilityTermExecutionStatus.Signed, v1Detail.ExecutionStatus);

        var v2Detail = await client.GetFromJsonAsync<ResponsibilityTermResponse>($"/api/responsibility-terms/{v2.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal(ResponsibilityTermStatus.Generated, v2Detail!.Status);
        Assert.Equal(ResponsibilityTermExecutionStatus.NotSent, v2Detail.ExecutionStatus);
        Assert.Null(v2Detail.Signature);
    }

    private static async Task<ResponsibilityTermResponse> GenerateTermAsync(HttpClient client, Guid handoverId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handoverId}/responsibility-terms", null, csrfToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<EquipmentHandoverDetailResponse> CreateFinalizedHandoverAsync(HttpClient client)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);
        finalizeResponse.EnsureSuccessStatusCode();
        return (await finalizeResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<PagedResponse<AuditLogResponse>> GetAuditPageAsync(HttpClient client, Guid entityId, string action)
    {
        var response = await client.GetAsync($"/api/audit?entityId={entityId}&action={action}");
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
