using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Handovers;

/// <summary>
/// "Registar envio" ("Registar Dispatch") on a Draft handover: registration
/// only before finalization, the resulting composition freeze
/// (<c>HandoverRules.CanModifyComposition</c>), the notification email, and
/// the invariant that dispatch never touches custody/lifecycle
/// (<c>EquipmentAssignment</c> is created exclusively by
/// <c>EquipmentHandoverService.FinalizeAsync</c>).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentDispatchTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task RegisterDispatch_OnDraftHandoverWithItem_Succeeds_AndDoesNotCreateAnAssignment()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, equipmentId, _) = await CreateDraftHandoverWithOneItemAsync(client);

        var response = await RegisterDispatchAsync(client, handover.Id, "2026-10-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dispatch = (await response.Content.ReadFromJsonAsync<EquipmentDispatchResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(new DateOnly(2026, 10, 1), dispatch.ExpectedDeliveryFromDate);

        var detail = await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>(
            $"/api/equipment-handovers/{handover.Id}", HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(detail!.Dispatch);
        Assert.Equal(dispatch.Id, detail.Dispatch!.Id);

        var equipment = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions);
        Assert.Null(equipment!.CurrentCustody);
        Assert.Equal(EquipmentStatus.InTransit, equipment.Status);
    }

    [Fact]
    public async Task RegisterDispatch_SendsOneNotificationEmailToTheConsultant()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, _, consultantEmail) = await CreateDraftHandoverWithOneItemAsync(client);
        GetEmailSender().Reset();

        var response = await RegisterDispatchAsync(client, handover.Id, "2026-10-01");
        response.EnsureSuccessStatusCode();

        var sent = Assert.Single(GetEmailSender().SentMessages, m => m.ToAddress == consultantEmail);
        Assert.Null(sent.Attachment);
        Assert.True(sent.IncludeSignature);

        // The approved copy, as actually sent: the delivery date stated once.
        Assert.Equal("Entrega de equipamento agendada", sent.Subject);
        Assert.Contains("Informamos que está prevista a entrega do equipamento abaixo para o dia 01/10/2026.", sent.PlainTextBody);
        Assert.DoesNotContain("prevista para", sent.PlainTextBody);
        Assert.DoesNotContain("prevista entre", sent.PlainTextBody);
        Assert.Single(sent.PlainTextBody.Split('\n'), line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegisterDispatch_OnFinalizedHandover_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, _, _) = await CreateDraftHandoverWithOneItemAsync(client);
        (await RegisterDispatchAsync(client, handover.Id, "2026-10-01")).EnsureSuccessStatusCode();
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var response = await RegisterDispatchAsync(client, handover.Id, "2026-10-01");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDispatch_Twice_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, _, _) = await CreateDraftHandoverWithOneItemAsync(client);
        (await RegisterDispatchAsync(client, handover.Id, "2026-10-01")).EnsureSuccessStatusCode();

        var response = await RegisterDispatchAsync(client, handover.Id, "2026-10-05");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AfterDispatch_AddingOrRemovingItems_IsLocked_ButFinalizeStillSucceeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, _, _) = await CreateDraftHandoverWithOneItemAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var extraEquipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        (await RegisterDispatchAsync(client, handover.Id, "2026-10-01")).EnsureSuccessStatusCode();

        var addResponse = await Phase4TestData.AddItemAsync(client, handover.Id, extraEquipment.Id);
        Assert.Equal(HttpStatusCode.Conflict, addResponse.StatusCode);

        var itemId = (await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>(
            $"/api/equipment-handovers/{handover.Id}", HttpClientTestExtensions.JsonOptions))!.Items.Single().Id;
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var removeResponse = await client.DeleteWithCsrfAsync($"/api/equipment-handovers/{handover.Id}/items/{itemId}", csrfToken);
        Assert.Equal(HttpStatusCode.Conflict, removeResponse.StatusCode);

        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);
        Assert.Equal(HttpStatusCode.OK, finalizeResponse.StatusCode);
    }

    [Fact]
    public async Task Finalize_AfterDispatch_StillCreatesTheAssignmentNormally()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, equipmentId, _) = await CreateDraftHandoverWithOneItemAsync(client);
        (await RegisterDispatchAsync(client, handover.Id, "2026-10-01")).EnsureSuccessStatusCode();

        var finalizeResponse = await Phase4TestData.FinalizeAsync(client, handover.Id);
        finalizeResponse.EnsureSuccessStatusCode();

        var equipment = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(equipment!.CurrentCustody);
        Assert.Equal(EquipmentStatus.Assigned, equipment.Status);
    }

    [Fact]
    public async Task RegisterDispatch_RequiresCsrfToken()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (handover, _, _) = await CreateDraftHandoverWithOneItemAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/equipment-handovers/{handover.Id}/dispatch", new { ExpectedDeliveryFromDate = "2026-10-01" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private RecordingEmailSender GetEmailSender() => factory.Services.GetRequiredService<RecordingEmailSender>();

    private static Task<HttpResponseMessage> RegisterDispatchAsync(HttpClient client, Guid handoverId, string expectedDeliveryFromDate) =>
        RegisterDispatchAsync(client, handoverId, expectedDeliveryFromDate, null);

    private static async Task<HttpResponseMessage> RegisterDispatchAsync(
        HttpClient client, Guid handoverId, string expectedDeliveryFromDate, string? expectedDeliveryToDate)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync(
            $"/api/equipment-handovers/{handoverId}/dispatch",
            new { ExpectedDeliveryFromDate = expectedDeliveryFromDate, ExpectedDeliveryToDate = expectedDeliveryToDate },
            csrfToken);
    }

    /// <summary>Carrier by default: dispatch only applies to Carrier deliveries (Iteration 3).</summary>
    private static async Task<(EquipmentHandoverDetailResponse Handover, Guid EquipmentId, string ConsultantEmail)> CreateDraftHandoverWithOneItemAsync(
        HttpClient client, string deliveryMethod = "Carrier")
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var consultant = await client.GetFromJsonAsync<ConsultantDetailResponse>($"/api/consultants/{consultantId}", HttpClientTestExtensions.JsonOptions);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, itemId) = await Phase4TestData.PrepareReadyToFinalizeAsync(
            client, consultantId, equipment.Id, photoCount: 0, deliveryMethod: deliveryMethod);
        var withItem = await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>(
            $"/api/equipment-handovers/{handover.Id}", HttpClientTestExtensions.JsonOptions);
        _ = itemId;

        return (withItem!, equipment.Id, consultant!.Email);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
