using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.Returns;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Returns;

/// <summary>
/// The return/return-inspection/damage workflow against a real PostgreSQL
/// database (docs/PROJECT_SPEC.md, section 12; Phase 5 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentReturnTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task FullReturnLifecycle_NoDamage_EquipmentBecomesAvailableAgain()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, equipmentId, assignmentId, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var equipmentAfterInitiate = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("ReturnPending", equipmentAfterInitiate.Status.ToString());

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 1, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);

        var receiveResponse = await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3);
        receiveResponse.EnsureSuccessStatusCode();
        var received = (await receiveResponse.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Received", received.Status.ToString());

        var equipmentAfterReceive = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("UnderInspection", equipmentAfterReceive.Status.ToString());

        var assignmentsResponse = await client.GetAsync($"/api/equipment-assignments?equipmentId={equipmentId}");
        var assignments = (await assignmentsResponse.Content.ReadFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(HttpClientTestExtensions.JsonOptions))!;
        var assignment = Assert.Single(assignments.Items);
        Assert.Equal("Ended", assignment.Status.ToString());
        Assert.NotNull(assignment.EndedAtUtc);

        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();

        var finalizeResponse = await Phase5TestData.FinalizeReturnInspectionAsync(client, returnId);
        finalizeResponse.EnsureSuccessStatusCode();
        var finalized = (await finalizeResponse.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal("Completed", finalized.Status.ToString());
        Assert.NotNull(finalized.CompletedAtUtc);
        Assert.NotNull(finalized.Inspection!.FinalizedAtUtc);
        Assert.False(finalized.Inspection.HasDamage);
        Assert.Null(finalized.Inspection.Damage);

        var equipmentAfterFinalize = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("Available", equipmentAfterFinalize.Status.ToString());
    }

    [Fact]
    public async Task FullReturnLifecycle_WithDamage_EquipmentBecomesDamagedWithOpenDamageRecord()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 2, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();

        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId, condition: "Poor", hasDamage: true, damageDescription: "Cracked screen"))
            .EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadDamagePhotoAsync(client, returnId)).EnsureSuccessStatusCode();

        var finalizeResponse = await Phase5TestData.FinalizeReturnInspectionAsync(client, returnId);
        finalizeResponse.EnsureSuccessStatusCode();
        var finalized = (await finalizeResponse.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal("Completed", finalized.Status.ToString());
        Assert.True(finalized.Inspection!.HasDamage);
        Assert.NotNull(finalized.Inspection.Damage);
        Assert.Equal("Open", finalized.Inspection.Damage!.Status.ToString());
        Assert.Equal("Cracked screen", finalized.Inspection.Damage.Description);
        Assert.Single(finalized.Inspection.Damage.Photos);

        var equipmentAfterFinalize = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("Damaged", equipmentAfterFinalize.Status.ToString());
    }

    [Fact]
    public async Task FinalizeInspection_WithDamageButNoDescriptionOrPhoto_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 3, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();

        // HasDamage = true but no description and no damage photo.
        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId, hasDamage: true)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();

        var finalizeResponse = await Phase5TestData.FinalizeReturnInspectionAsync(client, returnId);

        Assert.Equal(HttpStatusCode.Conflict, finalizeResponse.StatusCode);
    }

    [Fact]
    public async Task FinalizeInspection_LaptopWithFewerThanThreeOptionalInspectionPhotos_StillSucceeds()
    {
        // The former inspection-time 3-photo minimum was removed: the
        // official condition-photo refresh now happens at physical receipt
        // (see ReceiveReturn_RequiresExactlyThreePhotosForLaptop), and
        // ReturnInspectionPhoto is optional additional evidence only — HR is
        // never asked to photograph the same equipment twice for one Return.
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 4, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();

        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();

        var finalizeResponse = await Phase5TestData.FinalizeReturnInspectionAsync(client, returnId);

        Assert.Equal(HttpStatusCode.OK, finalizeResponse.StatusCode);
    }

    [Fact]
    public async Task ReceiveReturn_RequiresExactlyThreePhotosForLaptop()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua Receiver {Guid.NewGuid():N}, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);

        var tooFew = await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 2);
        Assert.Equal(HttpStatusCode.BadRequest, tooFew.StatusCode);

        var tooMany = await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 4);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        var none = await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 0);
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);

        var exact = await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3);
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
    }

    [Fact]
    public async Task ReceiveReturn_WithTheActorAsReceiver_PersistsTheActor()
    {
        // The normal path: the person recording the receipt is the one who received it.
        var (actorEmail, actorPassword) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var actorId = await ApiTestUsers.GetIdAsync(factory, actorEmail);
        using var client = factory.CreateClient();
        await client.LoginAsync(actorEmail, actorPassword);
        var (_, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var response = await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), actorId, photoCount: 3);

        response.EnsureSuccessStatusCode();
        var received = (await response.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Test Administration", received.ReceivedByDisplayNameSnapshot);
        Assert.Equal(actorId, await GetPersistedReceiverIdAsync(returnId));
    }

    [Fact]
    public async Task ReceiveReturn_WithAnotherUserAsReceiver_PersistsThatUser_NotTheActor()
    {
        // The receiver may differ from the actor: what is stored is what was submitted.
        var (actorEmail, actorPassword) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var actorId = await ApiTestUsers.GetIdAsync(factory, actorEmail);
        using var client = factory.CreateClient();
        await client.LoginAsync(actorEmail, actorPassword);
        var (_, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver Other, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);

        var response = await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3);

        response.EnsureSuccessStatusCode();
        var received = (await response.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Test HR", received.ReceivedByDisplayNameSnapshot);
        var persisted = await GetPersistedReceiverIdAsync(returnId);
        Assert.Equal(receiverId, persisted);
        Assert.NotEqual(actorId, persisted);
    }

    [Fact]
    public async Task ReceiveReturn_WithAnUnknownOrInactiveReceiver_IsRejected_AndTheReturnStaysPending()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var unknown = await Phase5TestData.ReceiveReturnAsync(client, returnId, today, Guid.NewGuid(), photoCount: 3);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var (inactiveEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver Inactive, Lisboa", isActive: false);
        var inactiveId = await ApiTestUsers.GetIdAsync(factory, inactiveEmail);
        var inactive = await Phase5TestData.ReceiveReturnAsync(client, returnId, today, inactiveId, photoCount: 3);
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);

        var detail = await client.GetFromJsonAsync<EquipmentReturnDetailResponse>($"/api/equipment-returns/{returnId}", HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Pending", detail!.Status.ToString());
        Assert.Null(detail.ReceivedByDisplayNameSnapshot);
        Assert.Null(await GetPersistedReceiverIdAsync(returnId));
        Assert.Equal("ReturnPending", (await GetEquipmentAsync(client, equipmentId)).Status.ToString());
    }

    [Fact]
    public async Task GenericEquipment_ReturnDoesNotRequirePhotos()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, itemId) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipment.Id);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 5, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 0)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();

        var finalizeResponse = await Phase5TestData.FinalizeReturnInspectionAsync(client, equipmentReturn.Id);

        Assert.Equal(HttpStatusCode.OK, finalizeResponse.StatusCode);
    }

    [Fact]
    public async Task InactiveConsultant_CanStillHaveActiveAssignmentReturned()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, equipmentId, assignmentId, _) = await SetUpAssignedLaptopWithoutReturnAsync(client);

        var deactivateCsrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync<object?>($"/api/consultants/{consultantId}/deactivate", null, deactivateCsrf)).EnsureSuccessStatusCode();

        var initiateResponse = await Phase5TestData.InitiateReturnAsync(client, assignmentId);
        Assert.Equal(HttpStatusCode.Created, initiateResponse.StatusCode);
        var equipmentReturn = (await initiateResponse.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 6, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        var receiveResponse = await Phase5TestData.ReceiveReturnAsync(client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3);

        Assert.Equal(HttpStatusCode.OK, receiveResponse.StatusCode);

        var equipmentAfterReceive = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("UnderInspection", equipmentAfterReceive.Status.ToString());
    }

    [Fact]
    public async Task ReassignmentAfterReturn_KeepsOriginalAssignmentHistoricalAndIndependent()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, assignmentIdA, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 7, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();

        // Reassign the now-Available equipment to a brand new Consultant B.
        var (consultantB, _) = await Phase2TestData.CreateConsultantAsync(client);
        var (handoverB, itemIdB) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantB, equipmentId, photoCount: 3);
        var finalizeB = await Phase4TestData.FinalizeAsync(client, handoverB.Id);
        finalizeB.EnsureSuccessStatusCode();

        var assignmentsResponse = await client.GetAsync($"/api/equipment-assignments?equipmentId={equipmentId}");
        var assignments = (await assignmentsResponse.Content.ReadFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(2, assignments.Items.Count);
        var original = assignments.Items.Single(a => a.Id == assignmentIdA);
        var replacement = assignments.Items.Single(a => a.Id != assignmentIdA);

        Assert.Equal("Ended", original.Status.ToString());
        Assert.Equal("Active", replacement.Status.ToString());
        Assert.Equal(consultantB, replacement.ConsultantId);

        // Only one Active assignment exists for this equipment.
        Assert.Single(assignments.Items, a => a.Status.ToString() == "Active");

        // The original return and handover remain independently readable and unaffected.
        var originalReturnResponse = await client.GetAsync($"/api/equipment-returns/{returnId}");
        var originalReturn = (await originalReturnResponse.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Completed", originalReturn.Status.ToString());
    }

    [Fact]
    public async Task FinalizedInspection_CannotBeModifiedOrHavePhotosChanged()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 8, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();
        var photoResponse = await Phase5TestData.UploadReturnPhotoAsync(client, returnId);
        photoResponse.EnsureSuccessStatusCode();
        var photo = (await photoResponse.Content.ReadFromJsonAsync<ReturnInspectionPhotoResponse>(HttpClientTestExtensions.JsonOptions))!;
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();

        var updateResponse = await Phase5TestData.RecordReturnInspectionAsync(client, returnId, condition: "Poor");
        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);

        var uploadResponse = await Phase5TestData.UploadReturnPhotoAsync(client, returnId);
        Assert.Equal(HttpStatusCode.Conflict, uploadResponse.StatusCode);

        var deleteCsrf = await client.GetAntiforgeryTokenAsync();
        var deleteResponse = await client.DeleteWithCsrfAsync($"/api/equipment-returns/{returnId}/inspection/photos/{photo.Id}", deleteCsrf);
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task DraftReturnPhoto_CanBeDeletedBeforeFinalization()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 9, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();
        var photoResponse = await Phase5TestData.UploadReturnPhotoAsync(client, returnId);
        var photo = (await photoResponse.Content.ReadFromJsonAsync<ReturnInspectionPhotoResponse>(HttpClientTestExtensions.JsonOptions))!;

        var deleteCsrf = await client.GetAntiforgeryTokenAsync();
        var deleteResponse = await client.DeleteWithCsrfAsync($"/api/equipment-returns/{returnId}/inspection/photos/{photo.Id}", deleteCsrf);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task ReturnPhotoContent_DownloadedBytesMatchUploadedBytes()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 10, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();
        var photoResponse = await Phase5TestData.UploadReturnPhotoAsync(client, returnId);
        var photo = (await photoResponse.Content.ReadFromJsonAsync<ReturnInspectionPhotoResponse>(HttpClientTestExtensions.JsonOptions))!;

        var contentResponse = await client.GetAsync($"/api/equipment-returns/{returnId}/inspection/photos/{photo.Id}/content");
        contentResponse.EnsureSuccessStatusCode();
        var downloadedBytes = await contentResponse.Content.ReadAsByteArrayAsync();

        Assert.Equal(Phase4TestData.MinimalJpegBytes, downloadedBytes);
    }

    [Fact]
    public async Task UploadReturnPhoto_WithFakeExtension_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 11, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();

        var notActuallyAnImage = "this is not an image"u8.ToArray();
        var response = await Phase5TestData.UploadReturnPhotoAsync(client, returnId, notActuallyAnImage, "fake.jpg", "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadReturnPhoto_RequiresCsrfToken()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 12, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Phase4TestData.MinimalJpegBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "no-csrf.jpg");

        var response = await client.PostAsync($"/api/equipment-returns/{returnId}/inspection/photos", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hr_CanRunTheFullReturnWorkflow_ButStillCannotAccessUsersOrAudit()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, assignmentId, _) = await SetUpAssignedLaptopWithoutReturnAsync(adminClient);

        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR Return, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var initiateResponse = await Phase5TestData.InitiateReturnAsync(hrClient, assignmentId);
        Assert.Equal(HttpStatusCode.Created, initiateResponse.StatusCode);
        var equipmentReturn = (await initiateResponse.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        var cancelResponse = await Phase5TestData.CancelReturnAsync(hrClient, equipmentReturn.Id, "No longer needed.");
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await hrClient.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hrClient.GetAsync("/api/audit")).StatusCode);

        var retireCsrf = await hrClient.GetAntiforgeryTokenAsync();
        var retireResponse = await hrClient.PostWithCsrfAsync<object?>($"/api/equipment/{equipmentId}/retire", null, retireCsrf);
        Assert.Equal(HttpStatusCode.Forbidden, retireResponse.StatusCode);
    }

    [Fact]
    public async Task ReturnAuditEvents_ContainNoConsultantPii()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var consultantResponse = await client.GetAsync($"/api/consultants/{consultantId}");
        var consultant = (await consultantResponse.Content.ReadFromJsonAsync<ConsultantDetailForAuditCheck>(HttpClientTestExtensions.JsonOptions))!;

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver 13, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId, hasDamage: true, damageDescription: "Sensitive-looking note about the laptop"))
            .EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadDamagePhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();

        var auditResponse = await client.GetAsync($"/api/audit?entityId={returnId}&page=1&pageSize=50");
        var page = (await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions))!;

        Assert.NotEmpty(page.Items);
        foreach (var entry in page.Items)
        {
            var combined = $"{entry.OldValuesJson}{entry.NewValuesJson}{entry.MetadataJson}";
            Assert.DoesNotContain(consultant.Nif, combined);
            Assert.DoesNotContain(consultant.Address, combined);
            Assert.DoesNotContain(consultant.Phone, combined);
            Assert.DoesNotContain("Sensitive-looking note", combined);
        }
    }

    [Fact]
    public async Task SecondReturn_ForSameAssignment_IsImpossibleEvenAfterFirstReturnIsCompleted()
    {
        // EquipmentAssignment 1 -> 0..1 *non-Cancelled* EquipmentReturn is a
        // lifetime invariant (see ADR-044): a Completed return
        // permanently occupies the assignment's one legitimate return slot —
        // unlike a Cancelled one (see the ReInitiateReturn_AfterCancellation_*
        // test below), it never releases the assignment for another attempt.
        // The application's own `assignment.Status != Active` check already
        // makes this unreachable through the normal API once the assignment
        // is Ended, so this test bypasses that check (simulating a
        // hypothetical future bug or manual data correction) to prove the
        // database's partial unique index on EquipmentReturns(EquipmentAssignmentId)
        // WHERE Status <> 'Cancelled' — not just the application check — is
        // the real backstop, and that it still counts a Completed row toward
        // the uniqueness (only Cancelled rows are excluded).
        using var client = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, assignmentId, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver Second Return, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();

        // Bypass the application-level guard directly at the database.
        await Phase5TestData.ForceAssignmentReactivatableStateAsync(factory, assignmentId, equipmentId);

        var secondInitiateResponse = await Phase5TestData.InitiateReturnAsync(client, assignmentId);

        Assert.Equal(HttpStatusCode.Conflict, secondInitiateResponse.StatusCode);

        var returnsResponse = await client.GetAsync($"/api/equipment-returns?equipmentId={equipmentId}");
        var returns = (await returnsResponse.Content.ReadFromJsonAsync<PagedResponse<EquipmentReturnSummaryResponse>>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Single(returns.Items);
    }

    [Fact]
    public async Task InitiateReturn_ConcurrentForSameAssignment_OnlyOneSucceeds()
    {
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var (_, _, assignmentId, _) = await SetUpAssignedLaptopWithoutReturnAsync(clientA);

        var results = await Task.WhenAll(
            Phase5TestData.InitiateReturnAsync(clientA, assignmentId),
            Phase5TestData.InitiateReturnAsync(clientB, assignmentId));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task ReceiveReturn_ConcurrentForSameReturn_OnlyOneSucceeds()
    {
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(clientA);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver Race, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var results = await Task.WhenAll(
            Phase5TestData.ReceiveReturnAsync(clientA, returnId, today, receiverId, photoCount: 3),
            Phase5TestData.ReceiveReturnAsync(clientB, returnId, today, receiverId, photoCount: 3));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        // The equipment/assignment side effects must have applied exactly once.
        var equipment = await GetEquipmentAsync(clientA, equipmentId);
        Assert.Equal("UnderInspection", equipment.Status.ToString());

        var assignmentsResponse = await clientA.GetAsync($"/api/equipment-assignments?equipmentId={equipmentId}&status=Ended");
        var assignments = (await assignmentsResponse.Content.ReadFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Single(assignments.Items);
    }

    [Fact]
    public async Task FinalizeInspection_ConcurrentForSameReturn_OnlyOneSucceeds()
    {
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(clientA);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver Race 2, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(clientA, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(clientA, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(clientA, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(clientA, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(clientA, returnId)).EnsureSuccessStatusCode();

        var results = await Task.WhenAll(
            Phase5TestData.FinalizeReturnInspectionAsync(clientA, returnId),
            Phase5TestData.FinalizeReturnInspectionAsync(clientB, returnId));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var equipment = await GetEquipmentAsync(clientA, equipmentId);
        Assert.Equal("Available", equipment.Status.ToString());
    }

    [Fact]
    public async Task CancelReturn_WhilePending_ReturnsEquipmentToAssignedAndKeepsAssignmentActive()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, assignmentId, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var cancelResponse = await Phase5TestData.CancelReturnAsync(client, returnId, "Return initiated in error.");
        cancelResponse.EnsureSuccessStatusCode();
        var cancelled = (await cancelResponse.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal("Cancelled", cancelled.Status.ToString());
        Assert.NotNull(cancelled.CancelledAtUtc);
        Assert.Equal("Return initiated in error.", cancelled.CancellationReason);
        Assert.NotNull(cancelled.CancelledByDisplayNameSnapshot);

        var equipment = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("Assigned", equipment.Status.ToString());

        var assignmentsResponse = await client.GetAsync($"/api/equipment-assignments?equipmentId={equipmentId}");
        var assignments = (await assignmentsResponse.Content.ReadFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(HttpClientTestExtensions.JsonOptions))!;
        var assignment = Assert.Single(assignments.Items);
        Assert.Equal("Active", assignment.Status.ToString());
        Assert.Equal(assignmentId, assignment.Id);
        Assert.Null(assignment.EndedAtUtc);

        // The cancelled return remains historical and queryable, never deleted.
        var returnDetailResponse = await client.GetAsync($"/api/equipment-returns/{returnId}");
        returnDetailResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CancelReturn_AfterPhysicalReceipt_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver Cancel 1, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();

        var cancelResponse = await Phase5TestData.CancelReturnAsync(client, returnId);

        Assert.Equal(HttpStatusCode.Conflict, cancelResponse.StatusCode);

        // Nothing about the already-Received state changed.
        var equipment = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("UnderInspection", equipment.Status.ToString());
    }

    [Fact]
    public async Task CancelReturn_AfterCompletion_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (_, _, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver Cancel 2, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, returnId, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, returnId)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, returnId)).EnsureSuccessStatusCode();

        var cancelResponse = await Phase5TestData.CancelReturnAsync(client, returnId);

        Assert.Equal(HttpStatusCode.Conflict, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task ReInitiateReturn_AfterCancellation_SucceedsAndCanProceedToCompletion()
    {
        // The key product behavior this amendment exists for: a cancelled
        // return releases the assignment for a brand-new, independent return
        // attempt — through the normal API, no test-only bypass required,
        // since cancellation never ends the assignment.
        using var client = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, assignmentId, returnId1) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);

        (await Phase5TestData.CancelReturnAsync(client, returnId1)).EnsureSuccessStatusCode();

        var equipmentAfterCancel = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("Assigned", equipmentAfterCancel.Status.ToString());

        var initiate2Response = await Phase5TestData.InitiateReturnAsync(client, assignmentId);
        Assert.Equal(HttpStatusCode.Created, initiate2Response.StatusCode);
        var return2 = (await initiate2Response.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Pending", return2.Status.ToString());
        Assert.NotEqual(returnId1, return2.Id);

        var equipmentAfterReinitiate = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("ReturnPending", equipmentAfterReinitiate.Status.ToString());

        // return #1 remains Cancelled, untouched by the new attempt.
        var return1Response = await client.GetAsync($"/api/equipment-returns/{returnId1}");
        var return1 = (await return1Response.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Cancelled", return1.Status.ToString());

        // Return #2 proceeds normally through Received -> Completed.
        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver Reinitiate, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, return2.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId, photoCount: 3)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, return2.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, return2.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, return2.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.UploadReturnPhotoAsync(client, return2.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, return2.Id)).EnsureSuccessStatusCode();

        var equipmentAfterFinalize = await GetEquipmentAsync(client, equipmentId);
        Assert.Equal("Available", equipmentAfterFinalize.Status.ToString());

        // After #2 is Completed, a third return for the same assignment must
        // remain impossible — the application guard and database invariant
        // agree (assignment is now Ended, matching SecondReturn_ForSameAssignment_*).
        var thirdInitiateResponse = await Phase5TestData.InitiateReturnAsync(client, assignmentId);
        Assert.Equal(HttpStatusCode.Conflict, thirdInitiateResponse.StatusCode);
    }

    [Fact]
    public async Task MultipleCancelledReturns_ForSameAssignment_AreStructurallyAllowed()
    {
        // Direct structural proof at the database/API boundary: cancelling
        // repeatedly must never itself be blocked by the unique index (only a
        // non-Cancelled row counts toward it).
        using var client = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, assignmentId, returnId1) = await SetUpAssignedLaptopWithReturnInitiatedAsync(client);
        (await Phase5TestData.CancelReturnAsync(client, returnId1)).EnsureSuccessStatusCode();

        var initiate2 = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);
        (await Phase5TestData.CancelReturnAsync(client, initiate2.Id)).EnsureSuccessStatusCode();

        var initiate3 = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);
        (await Phase5TestData.CancelReturnAsync(client, initiate3.Id)).EnsureSuccessStatusCode();

        var returnsResponse = await client.GetAsync($"/api/equipment-returns?equipmentId={equipmentId}");
        var returns = (await returnsResponse.Content.ReadFromJsonAsync<PagedResponse<EquipmentReturnSummaryResponse>>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(3, returns.Items.Count);
        Assert.All(returns.Items, r => Assert.Equal("Cancelled", r.Status.ToString()));

        // The assignment can still legitimately proceed to a real completed return afterward.
        var finalReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);
        Assert.Equal("Pending", finalReturn.Status.ToString());
    }

    [Fact]
    public async Task CancelReturn_ConcurrentWithReceive_OnlyOneSucceeds()
    {
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, assignmentId, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(clientA);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Receiver Cancel Race, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var results = await Task.WhenAll(
            Phase5TestData.CancelReturnAsync(clientA, returnId),
            Phase5TestData.ReceiveReturnAsync(clientB, returnId, today, receiverId, photoCount: 3));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        // Final state must be one of exactly two consistent outcomes, never a mix.
        var returnResponse = await clientA.GetAsync($"/api/equipment-returns/{returnId}");
        var equipmentReturn = (await returnResponse.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        var equipment = await GetEquipmentAsync(clientA, equipmentId);

        var assignmentsResponse = await clientA.GetAsync($"/api/equipment-assignments?equipmentId={equipmentId}");
        var assignments = (await assignmentsResponse.Content.ReadFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(HttpClientTestExtensions.JsonOptions))!;
        var assignment = Assert.Single(assignments.Items);
        Assert.Equal(assignmentId, assignment.Id);

        if (equipmentReturn.Status.ToString() == "Cancelled")
        {
            Assert.Equal("Assigned", equipment.Status.ToString());
            Assert.Equal("Active", assignment.Status.ToString());
        }
        else
        {
            Assert.Equal("Received", equipmentReturn.Status.ToString());
            Assert.Equal("UnderInspection", equipment.Status.ToString());
            Assert.Equal("Ended", assignment.Status.ToString());
        }
    }

    [Fact]
    public async Task CancelReturn_ConcurrentCancelAttempts_OnlyOneSucceeds()
    {
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var (_, equipmentId, _, returnId) = await SetUpAssignedLaptopWithReturnInitiatedAsync(clientA);

        var results = await Task.WhenAll(
            Phase5TestData.CancelReturnAsync(clientA, returnId),
            Phase5TestData.CancelReturnAsync(clientB, returnId));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var equipment = await GetEquipmentAsync(clientA, equipmentId);
        Assert.Equal("Assigned", equipment.Status.ToString());
    }

    [Fact]
    public async Task List_Search_MatchesConsultantFullNameOrAssetTag()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var token = $"zqy{Guid.NewGuid():N}";
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: $"Carla {token}");
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipment.Id);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);

        var byConsultant = await client.GetFromJsonAsync<PagedResponse<EquipmentReturnSummaryResponse>>(
            $"/api/equipment-returns?search={token.ToUpperInvariant()}", HttpClientTestExtensions.JsonOptions);
        Assert.Single(byConsultant!.Items);
        Assert.Equal(equipmentReturn.Id, byConsultant.Items[0].Id);

        var byAssetTag = await client.GetFromJsonAsync<PagedResponse<EquipmentReturnSummaryResponse>>(
            $"/api/equipment-returns?search={equipment.AssetTag}", HttpClientTestExtensions.JsonOptions);
        Assert.Single(byAssetTag!.Items);
        Assert.Equal(equipmentReturn.Id, byAssetTag.Items[0].Id);
    }

    private static async Task<(Guid ConsultantId, Guid EquipmentId, Guid AssignmentId, Guid ReturnId)> SetUpAssignedLaptopWithReturnInitiatedAsync(HttpClient client)
    {
        var (consultantId, equipmentId, assignmentId, _) = await SetUpAssignedLaptopWithoutReturnAsync(client);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);
        return (consultantId, equipmentId, assignmentId, equipmentReturn.Id);
    }

    private static async Task<(Guid ConsultantId, Guid EquipmentId, Guid AssignmentId, Guid HandoverId)> SetUpAssignedLaptopWithoutReturnAsync(HttpClient client)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client, $"Laptop {Guid.NewGuid():N}", "Laptop", "LT");
        var equipment = await Phase3TestData.CreateLaptopEquipmentAsync(client, equipmentType.Id);

        var (handover, itemId) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 3);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipment.Id);
        return (consultantId, equipment.Id, assignmentId, handover.Id);
    }

    /// <summary>The response only carries the receiver's name snapshot; the id itself is read from the database.</summary>
    private async Task<Guid?> GetPersistedReceiverIdAsync(Guid returnId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await dbContext.EquipmentReturns.AsNoTracking().FirstAsync(r => r.Id == returnId)).ReceivedByUserId;
    }

    private static async Task<EquipmentDetailResponse> GetEquipmentAsync(HttpClient client, Guid equipmentId)
    {
        var response = await client.GetAsync($"/api/equipment/{equipmentId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }

    private sealed record ConsultantDetailForAuditCheck(Guid Id, string FullName, string Nif, string Email, string Address, string Phone);
}
