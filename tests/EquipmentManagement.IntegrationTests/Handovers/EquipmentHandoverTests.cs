using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Handovers;

/// <summary>
/// Handover/assignment/delivery-inspection workflow against a real PostgreSQL
/// database (docs/PROJECT_SPEC.md, sections 9-11, 19-21; Phase 4 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentHandoverTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Hr_CanCreateDraftHandover_ForActiveConsultant()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);

        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);

        Assert.Equal("Draft", handover.Status.ToString());
        Assert.Empty(handover.Items);
        Assert.Null(handover.ConsultantSnapshot);
    }

    [Fact]
    public async Task CreateHandover_FailsForInactiveConsultant()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync<object?>($"/api/consultants/{consultantId}/deactivate", null, csrfToken);

        var response = await client.PostWithCsrfAsync(
            "/api/equipment-handovers", new { ConsultantId = consultantId, DeliveryMethod = "InPerson", Notes = (string?)null }, await client.GetAntiforgeryTokenAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AddItem_FailsWhenEquipmentIsNotAvailable()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var retireCsrf = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, retireCsrf);

        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);
        var response = await Phase4TestData.AddItemAsync(client, handover.Id, equipment.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AddItem_FailsWhenEquipmentAlreadyInSameHandover()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);
        (await Phase4TestData.AddItemAsync(client, handover.Id, equipment.Id)).EnsureSuccessStatusCode();

        var response = await Phase4TestData.AddItemAsync(client, handover.Id, equipment.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Finalize_FailsWithoutDeliveredOn()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);
        var addResponse = await Phase4TestData.AddItemAsync(client, handover.Id, equipment.Id);
        var withItem = (await addResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        (await Phase4TestData.RecordInspectionAsync(client, handover.Id, withItem.Items.Single().Id)).EnsureSuccessStatusCode();

        var response = await Phase4TestData.FinalizeAsync(client, handover.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Finalize_FailsWithoutInspection()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);
        (await Phase4TestData.AddItemAsync(client, handover.Id, equipment.Id)).EnsureSuccessStatusCode();
        (await Phase4TestData.SetDeliveredOnAsync(client, handover.Id, DateOnly.FromDateTime(DateTime.UtcNow))).EnsureSuccessStatusCode();

        var response = await Phase4TestData.FinalizeAsync(client, handover.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Finalize_LaptopFailsWithFewerThanThreePhotos()
    {
        // The Handover-level photo-upload requirement was removed: photos are
        // now an equipment-level "current condition" concept established at
        // equipment creation (or the legacy backfill action), not something
        // HR uploads per Handover. Uploading 0/1/2 Handover photos (the old,
        // now-vestigial, optional-evidence endpoint) no longer blocks
        // finalization by itself — only a Laptop/MobilePhone with NO current
        // EquipmentPhotoSet does. Since the normal creation API always
        // establishes one for Laptop/MobilePhone, this simulates the legacy
        // gap directly at the database level (same pattern as
        // Phase5TestData.ForceAssignmentReactivatableStateAsync).
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var laptopType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        var laptop = await Phase3TestData.CreateLaptopEquipmentAsync(client, laptopType.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<EquipmentManagement.Infrastructure.Persistence.ApplicationDbContext>();
            var photoSets = await dbContext.EquipmentPhotoSets.Where(s => s.EquipmentId == laptop.Id).ToListAsync();
            dbContext.EquipmentPhotoSets.RemoveRange(photoSets);
            await dbContext.SaveChangesAsync();
        }

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, laptop.Id, photoCount: 0);

        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);

        Assert.Equal(HttpStatusCode.Conflict, finalizeResponse.StatusCode);
    }

    [Fact]
    public async Task Finalize_LaptopWithNoHandoverPhotos_SucceedsUsingEquipmentCurrentPhotoSet()
    {
        // The new business rule, explicitly: a new Handover must NOT require
        // HR to upload delivery-condition photos at all — the equipment's own
        // current photo set (established at creation) is what Finalize checks
        // and freezes (EquipmentHandoverItem.EquipmentPhotoSetId).
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var laptopType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LQ");
        var laptop = await Phase3TestData.CreateLaptopEquipmentAsync(client, laptopType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, laptop.Id, photoCount: 0);

        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);

        Assert.Equal(HttpStatusCode.OK, finalizeResponse.StatusCode);
    }

    [Fact]
    public async Task Finalize_HappyPath_GenericEquipment_NoPhotosRequired()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);

        var response = await Phase4TestData.FinalizeAsync(client, handover.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var finalized = (await response.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Finalized", finalized.Status.ToString());
        Assert.NotNull(finalized.ConsultantSnapshot);
        Assert.NotNull(finalized.Items.Single().EquipmentSnapshot);

        var updatedEquipment = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipment.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Assigned", updatedEquipment!.Status.ToString());

        var assignments = await client.GetFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(
            $"/api/equipment-assignments?equipmentId={equipment.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Single(assignments!.Items);
        Assert.Equal("Active", assignments.Items[0].Status.ToString());
        Assert.Equal(consultantId, assignments.Items[0].ConsultantId);
    }

    [Fact]
    public async Task Finalize_HappyPath_Laptop_WithThreePhotos_CapturesFullSnapshot()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var laptopType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LX");
        var laptop = await Phase3TestData.CreateLaptopEquipmentAsync(client, laptopType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, laptop.Id, photoCount: 3);

        var response = await Phase4TestData.FinalizeAsync(client, handover.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var finalized = (await response.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        var itemSnapshot = finalized.Items.Single().EquipmentSnapshot!;
        Assert.NotNull(itemSnapshot.Processor);
        Assert.NotNull(itemSnapshot.Memory);
        Assert.NotNull(itemSnapshot.Storage);
        Assert.Equal(3, finalized.Items.Single().Inspection!.Photos.Count);
    }

    [Fact]
    public async Task Finalize_SecondAttemptOnAlreadyFinalizedHandover_Fails()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var secondAttempt = await Phase4TestData.FinalizeAsync(client, handover.Id);

        Assert.Equal(HttpStatusCode.Conflict, secondAttempt.StatusCode);
    }

    [Fact]
    public async Task Finalize_ConcurrentForSameEquipment_OnlyOneSucceeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantA, _) = await Phase2TestData.CreateConsultantAsync(client);
        var (consultantB, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var handoverA = await Phase4TestData.CreateDraftHandoverAsync(client, consultantA);
        var addA = await Phase4TestData.AddItemAsync(client, handoverA.Id, equipment.Id);
        addA.EnsureSuccessStatusCode();
        var itemA = (await addA.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!.Items.Single().Id;
        (await Phase4TestData.RecordInspectionAsync(client, handoverA.Id, itemA)).EnsureSuccessStatusCode();
        (await Phase4TestData.SetDeliveredOnAsync(client, handoverA.Id, DateOnly.FromDateTime(DateTime.UtcNow))).EnsureSuccessStatusCode();

        // Equipment is still Available at this point (neither draft has been
        // finalized yet) — a second draft can legitimately include it too.
        using var client2 = factory.CreateClient();
        var (adminEmail, adminPassword) = await ApiTestUsers.CreateAsync(factory, "Administration");
        await client2.LoginAsync(adminEmail, adminPassword);

        var handoverB = await Phase4TestData.CreateDraftHandoverAsync(client2, consultantB);
        var addB = await Phase4TestData.AddItemAsync(client2, handoverB.Id, equipment.Id);
        addB.EnsureSuccessStatusCode();
        var itemB = (await addB.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!.Items.Single().Id;
        (await Phase4TestData.RecordInspectionAsync(client2, handoverB.Id, itemB)).EnsureSuccessStatusCode();
        (await Phase4TestData.SetDeliveredOnAsync(client2, handoverB.Id, DateOnly.FromDateTime(DateTime.UtcNow))).EnsureSuccessStatusCode();

        var results = await Task.WhenAll(
            Phase4TestData.FinalizeAsync(client, handoverA.Id),
            Phase4TestData.FinalizeAsync(client2, handoverB.Id));

        var successCount = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);
    }

    [Fact]
    public async Task UploadPhoto_RejectsBytesThatAreNotAGenuineImage()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);
        var addResponse = await Phase4TestData.AddItemAsync(client, handover.Id, equipment.Id);
        var withItem = (await addResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        var itemId = withItem.Items.Single().Id;
        (await Phase4TestData.RecordInspectionAsync(client, handover.Id, itemId)).EnsureSuccessStatusCode();

        var fakeBytes = "this is definitely not an image"u8.ToArray();
        var response = await Phase4TestData.UploadPhotoAsync(client, handover.Id, itemId, fakeBytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadedPhoto_ContentRoundTrips()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);
        var addResponse = await Phase4TestData.AddItemAsync(client, handover.Id, equipment.Id);
        var withItem = (await addResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        var itemId = withItem.Items.Single().Id;
        (await Phase4TestData.RecordInspectionAsync(client, handover.Id, itemId)).EnsureSuccessStatusCode();

        var uploadResponse = await Phase4TestData.UploadPhotoAsync(client, handover.Id, itemId);
        uploadResponse.EnsureSuccessStatusCode();
        var photo = (await uploadResponse.Content.ReadFromJsonAsync<DeliveryInspectionPhotoResponse>(HttpClientTestExtensions.JsonOptions))!;

        var contentResponse = await client.GetAsync(
            $"/api/equipment-handovers/{handover.Id}/items/{itemId}/inspection/photos/{photo.Id}/content");
        contentResponse.EnsureSuccessStatusCode();
        var downloadedBytes = await contentResponse.Content.ReadAsByteArrayAsync();

        Assert.Equal(Phase4TestData.MinimalJpegBytes, downloadedBytes);
    }

    [Fact]
    public async Task RemoveItem_DeletesItemAndItsInspectionWhileDraft()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var handover = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId);
        var addResponse = await Phase4TestData.AddItemAsync(client, handover.Id, equipment.Id);
        var withItem = (await addResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        var itemId = withItem.Items.Single().Id;
        (await Phase4TestData.RecordInspectionAsync(client, handover.Id, itemId)).EnsureSuccessStatusCode();

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/equipment-handovers/{handover.Id}/items/{itemId}");
        deleteRequest.Headers.Add("X-CSRF-TOKEN", csrfToken);
        var deleteResponse = await client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>($"/api/equipment-handovers/{handover.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.Empty(afterDelete!.Items);
    }

    [Fact]
    public async Task Hr_CanRunTheFullWorkflowIncludingFinalization()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR Handover, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(hrClient);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(hrClient);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(hrClient, consultantId, equipment.Id, photoCount: 0);
        var response = await Phase4TestData.FinalizeAsync(hrClient, handover.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_Search_MatchesConsultantFullName_CaseInsensitively()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var token = $"zqx{Guid.NewGuid():N}";
        var (matchingConsultantId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Ana {token}");
        var (otherConsultantId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Bruno Unrelated {Guid.NewGuid():N}");

        var matching = await Phase4TestData.CreateDraftHandoverAsync(client, matchingConsultantId);
        await Phase4TestData.CreateDraftHandoverAsync(client, otherConsultantId);

        var response = await client.GetFromJsonAsync<PagedResponse<EquipmentHandoverSummaryResponse>>(
            $"/api/equipment-handovers?search={token.ToUpperInvariant()}", HttpClientTestExtensions.JsonOptions);

        Assert.Single(response!.Items);
        Assert.Equal(matching.Id, response.Items[0].Id);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
