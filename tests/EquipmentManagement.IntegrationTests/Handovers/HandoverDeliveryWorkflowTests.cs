using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Handovers;

/// <summary>
/// Iteration 3: Consultant eligibility (an Active allocation is
/// required for a new delivery, re-checked at dispatch and confirmation),
/// the explicit delivery method (InPerson vs Carrier) and its sequencing
/// rules, the legacy null-method state, and the pt-PT business errors that
/// "Confirmar entrega" (finalize) returns for the frontend to show.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class HandoverDeliveryWorkflowTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    // --- Consultant eligibility ---

    [Fact]
    public async Task Create_ForConsultantWithNoAllocation_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);

        var response = await PostCreateAsync(client, consultantId, "InPerson");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Registe primeiro uma alocação válida", await ReadTitleAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_ForConsultantWithOnlyEndedAllocations_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var allocation = await Phase4TestData.CreateAllocationAsync(client, consultantId, new DateOnly(2026, 1, 1));
        (await Phase4TestData.EndAllocationAsync(client, consultantId, allocation.Id, new DateOnly(2026, 6, 30))).EnsureSuccessStatusCode();

        var response = await PostCreateAsync(client, consultantId, "InPerson");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_ForConsultantWithCurrentActiveAllocation_Succeeds_AndPersistsDeliveryMethod()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        await Phase4TestData.CreateAllocationAsync(client, consultantId, new DateOnly(2026, 1, 1));

        var response = await PostCreateAsync(client, consultantId, "Carrier");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var handover = (await response.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(HandoverDeliveryMethod.Carrier, handover.DeliveryMethod);
        Assert.Equal(HandoverDeliveryMethod.Carrier, (await GetHandoverAsync(client, handover.Id)).DeliveryMethod);
    }

    /// <summary>
    /// A planned allocation (Active, StartDate still in the future) is a real,
    /// not-ended engagement under the existing allocation lifecycle, so the company
    /// may prepare and ship equipment before the first working day.
    /// </summary>
    [Fact]
    public async Task Create_ForConsultantWithOnlyAPlannedFutureAllocation_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        await Phase4TestData.CreateAllocationAsync(client, consultantId, DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2));

        var response = await PostCreateAsync(client, consultantId, "InPerson");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task StaleDraft_AfterAllocationEnded_DispatchIsRejected_AndDraftIsPreserved()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, allocationId, handoverId, equipmentId) = await CreateReadyDraftWithOwnAllocationAsync(client, "Carrier");
        (await Phase4TestData.EndAllocationAsync(client, consultantId, allocationId, new DateOnly(2026, 6, 30))).EnsureSuccessStatusCode();
        GetEmailSender().Reset();

        var response = await Phase4TestData.RegisterDispatchAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("não tem uma alocação válida", await ReadTitleAsync(response), StringComparison.Ordinal);
        var handover = await GetHandoverAsync(client, handoverId);
        Assert.Equal(HandoverStatus.Draft, handover.Status);
        Assert.Null(handover.Dispatch);
        Assert.Empty(GetEmailSender().SentMessages);
        await AssertNoCustodyAsync(client, equipmentId);
    }

    [Fact]
    public async Task StaleDraft_AfterAllocationEnded_ConfirmationIsRejected_AndDraftIsPreserved()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, allocationId, handoverId, equipmentId) = await CreateReadyDraftWithOwnAllocationAsync(client, "InPerson");
        (await Phase4TestData.EndAllocationAsync(client, consultantId, allocationId, new DateOnly(2026, 6, 30))).EnsureSuccessStatusCode();

        var response = await Phase4TestData.FinalizeAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("não tem uma alocação válida", await ReadTitleAsync(response), StringComparison.Ordinal);
        Assert.Equal(HandoverStatus.Draft, (await GetHandoverAsync(client, handoverId)).Status);
        await AssertNoCustodyAsync(client, equipmentId);
    }

    [Fact]
    public async Task HistoricalFinalizedHandover_RemainsReadable_AfterConsultantLosesEveryAllocation()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, allocationId, handoverId, equipmentId) = await CreateReadyDraftWithOwnAllocationAsync(client, "InPerson");
        (await Phase4TestData.FinalizeAsync(client, handoverId)).EnsureSuccessStatusCode();
        (await Phase4TestData.EndAllocationAsync(client, consultantId, allocationId, new DateOnly(2026, 6, 30))).EnsureSuccessStatusCode();

        var handover = await GetHandoverAsync(client, handoverId);

        Assert.Equal(HandoverStatus.Finalized, handover.Status);
        Assert.NotNull(handover.AllocationSnapshot);
        await AssertCustodyAsync(client, equipmentId);
    }

    /// <summary>
    /// The snapshot's professional context is the Consultant's single Active
    /// allocation, not whichever allocation has the latest StartDate: here an
    /// Ended allocation starts later than the Active one.
    /// </summary>
    [Fact]
    public async Task Confirmation_SnapshotsTheSingleActiveAllocation_NotTheLatestStartDate()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var laterResponse = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Papel Terminado",
            StartDate = "2026-08-01",
        }, csrfToken);
        laterResponse.EnsureSuccessStatusCode();
        var later = (await laterResponse.Content.ReadFromJsonAsync<EquipmentManagement.Api.Contracts.Allocations.ConsultantAllocationResponse>(
            HttpClientTestExtensions.JsonOptions))!;
        (await Phase4TestData.EndAllocationAsync(client, consultantId, later.Id, new DateOnly(2026, 8, 31))).EnsureSuccessStatusCode();
        await Phase4TestData.CreateAllocationAsync(client, consultantId, new DateOnly(2026, 1, 1));

        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);

        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var finalized = await GetHandoverAsync(client, handover.Id);
        Assert.Equal("Backend Developer", finalized.AllocationSnapshot!.RoleOrFunction);
    }

    [Fact]
    public async Task ConsultantList_HasActiveAllocationFilter_ReturnsOnlyEligibleConsultants()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var token = Guid.NewGuid().ToString("N")[..10];
        var (eligibleId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Elegivel {token}");
        await Phase4TestData.CreateAllocationAsync(client, eligibleId, new DateOnly(2026, 1, 1));
        var (endedId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Terminado {token}");
        var ended = await Phase4TestData.CreateAllocationAsync(client, endedId, new DateOnly(2026, 1, 1));
        (await Phase4TestData.EndAllocationAsync(client, endedId, ended.Id, new DateOnly(2026, 6, 30))).EnsureSuccessStatusCode();
        var (noAllocationId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"SemAlocacao {token}");

        var filtered = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?search={token}&hasActiveAllocation=true", HttpClientTestExtensions.JsonOptions);
        var unfiltered = await client.GetFromJsonAsync<PagedResponse<ConsultantSummaryResponse>>(
            $"/api/consultants?search={token}", HttpClientTestExtensions.JsonOptions);

        Assert.Equal([eligibleId], filtered!.Items.Select(c => c.Id).ToArray());
        Assert.Equal(3, unfiltered!.Items.Count);
        Assert.Contains(unfiltered.Items, c => c.Id == noAllocationId);
    }

    // --- Delivery method: creation and lock ---

    [Fact]
    public async Task Create_WithoutDeliveryMethod_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        await Phase4TestData.CreateAllocationAsync(client, consultantId, new DateOnly(2026, 1, 1));

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync("/api/equipment-handovers", new { ConsultantId = consultantId }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeliveryMethod_CanChangeBeforeDispatch_ButIsLockedAfterDispatch()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, handoverId, _) = await CreateReadyDraftWithOwnAllocationAsync(client, "InPerson");

        var toCarrier = await Phase4TestData.SetDeliveryMethodAsync(client, handoverId, "Carrier");
        Assert.Equal(HttpStatusCode.OK, toCarrier.StatusCode);
        Assert.Equal(HandoverDeliveryMethod.Carrier, (await GetHandoverAsync(client, handoverId)).DeliveryMethod);

        (await Phase4TestData.RegisterDispatchAsync(client, handoverId)).EnsureSuccessStatusCode();

        var backToInPerson = await Phase4TestData.SetDeliveryMethodAsync(client, handoverId, "InPerson");
        Assert.Equal(HttpStatusCode.Conflict, backToInPerson.StatusCode);
        Assert.Equal(HandoverDeliveryMethod.Carrier, (await GetHandoverAsync(client, handoverId)).DeliveryMethod);
    }

    [Fact]
    public async Task DeliveryMethod_CannotChangeOnFinalizedHandover()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, handoverId, _) = await CreateReadyDraftWithOwnAllocationAsync(client, "InPerson");
        (await Phase4TestData.FinalizeAsync(client, handoverId)).EnsureSuccessStatusCode();

        var response = await Phase4TestData.SetDeliveryMethodAsync(client, handoverId, "Carrier");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- In-person workflow ---

    [Fact]
    public async Task InPerson_CannotRegisterDispatch_AndNoEmailIsSent()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, handoverId, _) = await CreateReadyDraftWithOwnAllocationAsync(client, "InPerson");
        GetEmailSender().Reset();

        var response = await Phase4TestData.RegisterDispatchAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("transportadora", await ReadTitleAsync(response), StringComparison.Ordinal);
        Assert.Null((await GetHandoverAsync(client, handoverId)).Dispatch);
        Assert.Empty(GetEmailSender().SentMessages);
    }

    [Fact]
    public async Task InPerson_ConfirmsWithoutDispatch_AndCustodyStartsOnlyAtConfirmation()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, handoverId, equipmentId) = await CreateReadyDraftWithOwnAllocationAsync(client, "InPerson");
        await AssertNoCustodyAsync(client, equipmentId);

        var response = await Phase4TestData.FinalizeAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var handover = await GetHandoverAsync(client, handoverId);
        Assert.Equal(HandoverStatus.Finalized, handover.Status);
        Assert.NotNull(handover.FinalizedAtUtc);
        Assert.Null(handover.Dispatch);
        await AssertCustodyAsync(client, equipmentId);
    }

    // --- Carrier workflow ---

    [Fact]
    public async Task Carrier_WithoutDispatch_CannotBeConfirmed_AndDraftIsPreserved()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, handoverId, equipmentId) = await CreateReadyDraftWithOwnAllocationAsync(client, "Carrier");

        var response = await Phase4TestData.FinalizeAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("O envio ainda não foi registado", await ReadTitleAsync(response), StringComparison.Ordinal);
        var handover = await GetHandoverAsync(client, handoverId);
        Assert.Equal(HandoverStatus.Draft, handover.Status);
        Assert.Null(handover.Dispatch);
        await AssertNoCustodyAsync(client, equipmentId);
    }

    [Fact]
    public async Task Carrier_FullSequence_DispatchEmailsWithoutCustody_ThenConfirmationCreatesAssignment()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _, handoverId, equipmentId) = await CreateReadyDraftWithOwnAllocationAsync(client, "Carrier");
        var consultant = await client.GetFromJsonAsync<ConsultantDetailResponse>($"/api/consultants/{consultantId}", HttpClientTestExtensions.JsonOptions);
        GetEmailSender().Reset();

        (await Phase4TestData.RegisterDispatchAsync(client, handoverId)).EnsureSuccessStatusCode();

        Assert.Single(GetEmailSender().SentMessages, m => m.ToAddress == consultant!.Email);
        Assert.NotNull((await GetHandoverAsync(client, handoverId)).Dispatch);
        await AssertNoCustodyAsync(client, equipmentId, EquipmentStatus.InTransit);

        var response = await Phase4TestData.FinalizeAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HandoverStatus.Finalized, (await GetHandoverAsync(client, handoverId)).Status);
        await AssertCustodyAsync(client, equipmentId);
    }

    // --- Legacy handovers with no recorded delivery method ---

    [Fact]
    public async Task LegacyFinalizedHandover_WithNullDeliveryMethod_RemainsReadable()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, handoverId, _) = await CreateReadyDraftWithOwnAllocationAsync(client, "InPerson");
        (await Phase4TestData.FinalizeAsync(client, handoverId)).EnsureSuccessStatusCode();
        await ForceLegacyNullDeliveryMethodAsync(handoverId);

        var handover = await GetHandoverAsync(client, handoverId);

        Assert.Equal(HandoverStatus.Finalized, handover.Status);
        Assert.Null(handover.DeliveryMethod);
    }

    [Fact]
    public async Task LegacyDraft_WithNullDeliveryMethod_MustChooseOneBeforeDispatchOrConfirmation()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, handoverId, equipmentId) = await CreateReadyDraftWithOwnAllocationAsync(client, "InPerson");
        await ForceLegacyNullDeliveryMethodAsync(handoverId);

        var dispatch = await Phase4TestData.RegisterDispatchAsync(client, handoverId);
        var finalize = await Phase4TestData.FinalizeAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.Conflict, dispatch.StatusCode);
        Assert.Contains("Selecione o método de entrega", await ReadTitleAsync(dispatch), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, finalize.StatusCode);
        Assert.Contains("Selecione o método de entrega", await ReadTitleAsync(finalize), StringComparison.Ordinal);
        Assert.Equal(HandoverStatus.Draft, (await GetHandoverAsync(client, handoverId)).Status);
        await AssertNoCustodyAsync(client, equipmentId);

        (await Phase4TestData.SetDeliveryMethodAsync(client, handoverId, "InPerson")).EnsureSuccessStatusCode();
        var retry = await Phase4TestData.FinalizeAsync(client, handoverId);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        await AssertCustodyAsync(client, equipmentId);
    }

    // --- Confirmation errors surfaced to the frontend ---

    [Fact]
    public async Task Confirm_WithLaptopsMissingCurrentPhotos_IsRejected_NamingEveryAssetTag_AndDraftIsPreserved()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var laptopType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LR");
        var laptopA = await Phase3TestData.CreateLaptopEquipmentAsync(client, laptopType.Id);
        var laptopB = await Phase3TestData.CreateLaptopEquipmentAsync(client, laptopType.Id);
        await RemoveCurrentPhotoSetsAsync(laptopA.Id, laptopB.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, laptopA.Id, photoCount: 0);
        var addResponse = await Phase4TestData.AddItemAsync(client, handover.Id, laptopB.Id);
        addResponse.EnsureSuccessStatusCode();
        var secondItemId = (await addResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!
            .Items.Single(i => i.EquipmentId == laptopB.Id).Id;
        (await Phase4TestData.RecordInspectionAsync(client, handover.Id, secondItemId)).EnsureSuccessStatusCode();

        var response = await Phase4TestData.FinalizeAsync(client, handover.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var title = await ReadTitleAsync(response);
        Assert.StartsWith("Não é possível confirmar esta entrega.", title, StringComparison.Ordinal);
        Assert.Contains(laptopA.AssetTag, title, StringComparison.Ordinal);
        Assert.Contains(laptopB.AssetTag, title, StringComparison.Ordinal);
        Assert.Equal(HandoverStatus.Draft, (await GetHandoverAsync(client, handover.Id)).Status);
        await AssertNoCustodyAsync(client, laptopA.Id);
        await AssertNoCustodyAsync(client, laptopB.Id);
    }

    private RecordingEmailSender GetEmailSender() => factory.Services.GetRequiredService<RecordingEmailSender>();

    private static async Task<HttpResponseMessage> PostCreateAsync(HttpClient client, Guid consultantId, string deliveryMethod)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync(
            "/api/equipment-handovers", new { ConsultantId = consultantId, DeliveryMethod = deliveryMethod }, csrfToken);
    }

    /// <summary>Consultant with an explicitly created Active allocation (so tests can end it), and a Draft handover with one inspected, dated Generic item.</summary>
    private static async Task<(Guid ConsultantId, Guid AllocationId, Guid HandoverId, Guid EquipmentId)> CreateReadyDraftWithOwnAllocationAsync(
        HttpClient client, string deliveryMethod)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var allocation = await Phase4TestData.CreateAllocationAsync(client, consultantId, new DateOnly(2026, 1, 1));
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(
            client, consultantId, equipment.Id, photoCount: 0, deliveryMethod: deliveryMethod);
        return (consultantId, allocation.Id, handover.Id, equipment.Id);
    }

    private static async Task<EquipmentHandoverDetailResponse> GetHandoverAsync(HttpClient client, Guid handoverId) =>
        (await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>($"/api/equipment-handovers/{handoverId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task AssertNoCustodyAsync(HttpClient client, Guid equipmentId, EquipmentStatus expectedStatus = EquipmentStatus.Available)
    {
        var equipment = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions);
        Assert.Null(equipment!.CurrentCustody);
        Assert.Equal(expectedStatus, equipment.Status);
    }

    private static async Task AssertCustodyAsync(HttpClient client, Guid equipmentId)
    {
        var equipment = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(equipment!.CurrentCustody);
        Assert.Equal(EquipmentStatus.Assigned, equipment.Status);
    }

    private static async Task<string> ReadTitleAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("title").GetString()!;
    }

    /// <summary>Simulates a handover created before Round 3 (never backfilled), directly at the database level.</summary>
    private async Task ForceLegacyNullDeliveryMethodAsync(Guid handoverId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.EquipmentHandovers.Where(h => h.Id == handoverId)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.DeliveryMethod, (HandoverDeliveryMethod?)null));
    }

    /// <summary>Simulates legacy equipment that predates condition photos (same pattern as <c>EquipmentHandoverTests.Finalize_LaptopFailsWithFewerThanThreePhotos</c>).</summary>
    private async Task RemoveCurrentPhotoSetsAsync(params Guid[] equipmentIds)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var photoSets = await dbContext.EquipmentPhotoSets.Where(s => equipmentIds.Contains(s.EquipmentId)).ToListAsync();
        dbContext.EquipmentPhotoSets.RemoveRange(photoSets);
        await dbContext.SaveChangesAsync();
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
