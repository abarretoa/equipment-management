using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.ResponsibilityTerms;

/// <summary>
/// Responsibility Term generation/versioning/download against a real
/// PostgreSQL database (docs/PROJECT_SPEC.md, section 11; Phase 6 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class ResponsibilityTermTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Generate_FailsWhenHandoverIsNotFinalized()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);
        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);

        var response = await GenerateAsync(client, handover.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Generate_OnFinalizedHandover_ProducesFirstVersion()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);

        var response = await GenerateAsync(client, handover.Id);
        response.EnsureSuccessStatusCode();
        var term = (await response.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Matches("^TR-[0-9]{4}-[0-9]{6,}$", term.DocumentNumber);
        Assert.Equal(1, term.Version);
        Assert.Equal(EquipmentManagement.Domain.ResponsibilityTerms.ResponsibilityTermStatus.Generated, term.Status);
        Assert.Equal(handover.Id, term.EquipmentHandoverId);
    }

    [Fact]
    public async Task Download_ReturnsValidPdfMatchingRecordedHashAndSize_WithSafeFileName()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, consultant) = await CreateFinalizedHandoverWithConsultantAsync(client);

        var generateResponse = await GenerateAsync(client, handover.Id);
        generateResponse.EnsureSuccessStatusCode();
        var term = (await generateResponse.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;

        var contentResponse = await client.GetAsync($"/api/responsibility-terms/{term.Id}/content");
        contentResponse.EnsureSuccessStatusCode();

        Assert.Equal("application/pdf", contentResponse.Content.Headers.ContentType!.MediaType);
        var bytes = await contentResponse.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Equal(term.SizeBytes, bytes.LongLength);
        Assert.Equal(term.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));

        var dispositionValue = contentResponse.Content.Headers.ContentDisposition!.ToString();
        Assert.DoesNotContain(consultant.Nif, dispositionValue);
        Assert.DoesNotContain(consultant.FullName, dispositionValue);
        Assert.Contains(term.DocumentNumber, dispositionValue);
    }

    [Fact]
    public async Task Generate_AfterLiveConsultantAndEquipmentMutation_StillReflectsSnapshotCapturedAtFinalization()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: "Original Consultant Name");
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);
        finalizeResponse.EnsureSuccessStatusCode();
        var finalized = (await finalizeResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        var originalConsultantSnapshot = finalized.ConsultantSnapshot!;
        var originalEquipmentSnapshot = finalized.Items.Single().EquipmentSnapshot!;

        // Mutate the LIVE consultant and equipment after finalization.
        var updateConsultantCsrf = await client.GetAntiforgeryTokenAsync();
        var updateConsultantResponse = await client.PutWithCsrfAsync($"/api/consultants/{consultantId}", new
        {
            FullName = "Mutated Consultant Name",
            Nif = originalConsultantSnapshot.Nif,
            Email = $"mutated.{Guid.NewGuid():N}@example.invalid",
            Address = "Mutated Address 999",
            Phone = "+351911111111",
        }, updateConsultantCsrf);
        updateConsultantResponse.EnsureSuccessStatusCode();

        var updateEquipmentCsrf = await client.GetAntiforgeryTokenAsync();
        var updateEquipmentResponse = await client.PutWithCsrfAsync($"/api/equipment/{equipment.Id}", new
        {
            Manufacturer = "MutatedManufacturer",
            Model = "MutatedModel",
            SerialNumber = equipment.SerialNumber,
            Notes = (string?)null,
            LaptopSpecification = (object?)null,
        }, updateEquipmentCsrf);
        updateEquipmentResponse.EnsureSuccessStatusCode();

        // The handover's own snapshot must be unaffected by the live mutation —
        // this is the data the Responsibility Term is built from.
        var handoverAfterMutation = await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>(
            $"/api/equipment-handovers/{handover.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Original Consultant Name", handoverAfterMutation!.ConsultantSnapshot!.FullName);
        Assert.Equal(originalEquipmentSnapshot.Manufacturer, handoverAfterMutation.Items.Single().EquipmentSnapshot!.Manufacturer);
        Assert.Equal(originalEquipmentSnapshot.Model, handoverAfterMutation.Items.Single().EquipmentSnapshot!.Model);

        // Generating now must still succeed and produce a document rooted in
        // the untouched snapshot, not a fresh live lookup.
        var generateResponse = await GenerateAsync(client, handover.Id);
        generateResponse.EnsureSuccessStatusCode();
        var term = (await generateResponse.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(1, term.Version);

        var contentResponse = await client.GetAsync($"/api/responsibility-terms/{term.Id}/content");
        contentResponse.EnsureSuccessStatusCode();
        var bytes = await contentResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(term.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task Regenerate_SupersedesPreviousVersion_BothRemainDownloadableAndByteStable()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);

        var firstResponse = await GenerateAsync(client, handover.Id);
        firstResponse.EnsureSuccessStatusCode();
        var v1 = (await firstResponse.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;

        var secondResponse = await GenerateAsync(client, handover.Id);
        secondResponse.EnsureSuccessStatusCode();
        var v2 = (await secondResponse.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(2, v2.Version);

        var listResponse = await client.GetAsync($"/api/equipment-handovers/{handover.Id}/responsibility-terms");
        listResponse.EnsureSuccessStatusCode();
        var terms = (await listResponse.Content.ReadFromJsonAsync<List<ResponsibilityTermResponse>>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(2, terms.Count);
        Assert.Equal(v2.Version, terms[0].Version);
        Assert.Equal(v1.Version, terms[1].Version);
        Assert.Equal(EquipmentManagement.Domain.ResponsibilityTerms.ResponsibilityTermStatus.Superseded, terms[1].Status);
        Assert.Equal(EquipmentManagement.Domain.ResponsibilityTerms.ResponsibilityTermStatus.Generated, terms[0].Status);
        Assert.Single(terms, t => t.Status == EquipmentManagement.Domain.ResponsibilityTerms.ResponsibilityTermStatus.Generated);

        // v1 remains downloadable and byte-stable against its own recorded hash.
        var v1ContentResponse = await client.GetAsync($"/api/responsibility-terms/{v1.Id}/content");
        v1ContentResponse.EnsureSuccessStatusCode();
        var v1Bytes = await v1ContentResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(v1.Sha256, Convert.ToHexStringLower(SHA256.HashData(v1Bytes)));

        var v2ContentResponse = await client.GetAsync($"/api/responsibility-terms/{v2.Id}/content");
        v2ContentResponse.EnsureSuccessStatusCode();
        var v2Bytes = await v2ContentResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(v2.Sha256, Convert.ToHexStringLower(SHA256.HashData(v2Bytes)));
    }

    [Fact]
    public async Task Generate_ConcurrentForSameHandover_NeverProducesADuplicateVersionOrTwoCurrentTerms()
    {
        // Unlike Phase 5's return races, this operation does real PDF
        // rendering and a private-storage write *before* opening its
        // Serializable transaction (ADR-049) — the version number that could
        // collide is decided long before the narrow transactional window, so
        // two genuinely concurrent calls are not guaranteed to collide on the
        // same version: either can land as "1 succeeds outright, 1 gets 409
        // Concurrency and must retry" (a true race on the same version) or as
        // "both succeed sequentially" (versions 1 and 2, the second
        // correctly superseding the first) if their transactions don't
        // overlap. Both are correct; what must never happen is a duplicate
        // version or two rows simultaneously Status == Generated.
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(clientA);

        var results = await Task.WhenAll(
            GenerateAsync(clientA, handover.Id),
            GenerateAsync(clientB, handover.Id));

        Assert.All(results, r => Assert.True(
            r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Unexpected status {r.StatusCode}."));
        Assert.True(results.Count(r => r.StatusCode == HttpStatusCode.Created) is 1 or 2);

        var listResponse = await clientA.GetAsync($"/api/equipment-handovers/{handover.Id}/responsibility-terms");
        listResponse.EnsureSuccessStatusCode();
        var terms = (await listResponse.Content.ReadFromJsonAsync<List<ResponsibilityTermResponse>>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(terms.Select(t => t.Version).Distinct().Count(), terms.Count);
        Assert.Single(terms, t => t.Status == EquipmentManagement.Domain.ResponsibilityTerms.ResponsibilityTermStatus.Generated);
    }

    [Fact]
    public async Task Generate_RequiresAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/equipment-handovers/00000000-0000-0000-0000-000000000000/responsibility-terms", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Hr_CanGenerateAndReadAndDownload_SameAsAdministration()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR Responsibility Term, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var handover = await CreateFinalizedHandoverAsync(hrClient);

        var generateResponse = await GenerateAsync(hrClient, handover.Id);
        Assert.Equal(HttpStatusCode.Created, generateResponse.StatusCode);
        var term = (await generateResponse.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;

        var getResponse = await hrClient.GetAsync($"/api/responsibility-terms/{term.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var contentResponse = await hrClient.GetAsync($"/api/responsibility-terms/{term.Id}/content");
        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
    }

    [Fact]
    public async Task Generate_RecordsAuditEvent_AndRegenerateAlsoRecordsSuperseded_AndDownloadIsAudited()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);

        var firstResponse = await GenerateAsync(client, handover.Id);
        firstResponse.EnsureSuccessStatusCode();
        var v1 = (await firstResponse.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;

        var generatedAudit = await GetAuditPageAsync(client, v1.Id, "ResponsibilityTerm.Generated");
        var generatedEntry = Assert.Single(generatedAudit.Items);
        var generatedCombined = $"{generatedEntry.OldValuesJson}{generatedEntry.NewValuesJson}{generatedEntry.MetadataJson}";
        Assert.DoesNotContain("storageKey", generatedCombined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StorageKey", generatedCombined, StringComparison.Ordinal);

        var secondResponse = await GenerateAsync(client, handover.Id);
        secondResponse.EnsureSuccessStatusCode();

        var supersededAudit = await GetAuditPageAsync(client, v1.Id, "ResponsibilityTerm.Superseded");
        Assert.Single(supersededAudit.Items);

        var contentResponse = await client.GetAsync($"/api/responsibility-terms/{v1.Id}/content");
        contentResponse.EnsureSuccessStatusCode();

        var downloadedAudit = await GetAuditPageAsync(client, v1.Id, "ResponsibilityTerm.Downloaded");
        Assert.Single(downloadedAudit.Items);
    }

    [Fact]
    public async Task ResponseBodies_NeverExposeStorageKey()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var handover = await CreateFinalizedHandoverAsync(client);

        var generateResponse = await GenerateAsync(client, handover.Id);
        generateResponse.EnsureSuccessStatusCode();
        var generateBody = await generateResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storageKey", generateBody, StringComparison.OrdinalIgnoreCase);

        var term = (await JsonContentToTermAsync(generateBody))!;

        var getBody = await (await client.GetAsync($"/api/responsibility-terms/{term.Id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("storageKey", getBody, StringComparison.OrdinalIgnoreCase);

        var listBody = await (await client.GetAsync($"/api/equipment-handovers/{handover.Id}/responsibility-terms")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("storageKey", listBody, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            typeof(ResponsibilityTermResponse).GetProperties(),
            p => string.Equals(p.Name, "StorageKey", StringComparison.OrdinalIgnoreCase));
    }

    private static Task<ResponsibilityTermResponse?> JsonContentToTermAsync(string json) =>
        Task.FromResult(System.Text.Json.JsonSerializer.Deserialize<ResponsibilityTermResponse>(json, HttpClientTestExtensions.JsonOptions));

    private static async Task<HttpResponseMessage> GenerateAsync(HttpClient client, Guid handoverId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handoverId}/responsibility-terms", null, csrfToken);
    }

    private static async Task<EquipmentHandoverDetailResponse> CreateFinalizedHandoverAsync(HttpClient client)
    {
        var (handover, _) = await CreateFinalizedHandoverWithConsultantAsync(client);
        return handover;
    }

    private static async Task<(EquipmentHandoverDetailResponse Handover, ConsultantDetailResponse Consultant)> CreateFinalizedHandoverWithConsultantAsync(
        HttpClient client)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var consultant = await client.GetFromJsonAsync<ConsultantDetailResponse>($"/api/consultants/{consultantId}", HttpClientTestExtensions.JsonOptions);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);
        finalizeResponse.EnsureSuccessStatusCode();
        var finalized = (await finalizeResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        return (finalized, consultant!);
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
