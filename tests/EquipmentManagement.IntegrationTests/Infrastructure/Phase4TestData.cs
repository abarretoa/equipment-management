using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Allocations;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Domain.Allocations;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Creates handovers/items/inspections/photos through the real HTTP API for
/// integration test setup — same rationale as <see cref="Phase3TestData"/>.
/// </summary>
public static class Phase4TestData
{
    /// <summary>The smallest byte sequence <see cref="EquipmentManagement.Domain.Files.ImageSignatureDetector"/> recognizes as a genuine JPEG.</summary>
    public static readonly byte[] MinimalJpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    /// <summary>
    /// Arranges an eligible Consultant first (Iteration 3: a new
    /// handover requires an Active allocation), unless the test already gave
    /// them one. Tests of the eligibility rule itself post to the endpoint
    /// directly instead of using this helper.
    /// </summary>
    public static async Task<EquipmentHandoverDetailResponse> CreateDraftHandoverAsync(
        HttpClient client, Guid consultantId, string? notes = null, string deliveryMethod = "InPerson")
    {
        await EnsureEligibleAllocationAsync(client, consultantId);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync(
            "/api/equipment-handovers", new { ConsultantId = consultantId, DeliveryMethod = deliveryMethod, Notes = notes }, csrfToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    /// <summary>Gives the Consultant an open-ended Active Internal allocation, unless they already have an Active one.</summary>
    public static async Task EnsureEligibleAllocationAsync(HttpClient client, Guid consultantId)
    {
        var allocations = await client.GetFromJsonAsync<PagedResponse<ConsultantAllocationResponse>>(
            $"/api/consultants/{consultantId}/allocations?pageSize=100", HttpClientTestExtensions.JsonOptions);
        if (allocations!.Items.Any(a => a.Status == AllocationStatus.Active))
        {
            return;
        }

        await CreateAllocationAsync(client, consultantId, new DateOnly(2026, 1, 1));
    }

    public static async Task<ConsultantAllocationResponse> CreateAllocationAsync(HttpClient client, Guid consultantId, DateOnly startDate, DateOnly? endDate = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Backend Developer",
            StartDate = startDate,
            EndDate = endDate,
        }, csrfToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    public static async Task<HttpResponseMessage> EndAllocationAsync(HttpClient client, Guid consultantId, Guid allocationId, DateOnly endDate)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync(
            $"/api/consultants/{consultantId}/allocations/{allocationId}/end", new { EndDate = endDate, IsEndOfEngagement = false }, csrfToken);
    }

    public static async Task<HttpResponseMessage> SetDeliveryMethodAsync(HttpClient client, Guid handoverId, string deliveryMethod)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PutWithCsrfAsync($"/api/equipment-handovers/{handoverId}/delivery-method", new { DeliveryMethod = deliveryMethod }, csrfToken);
    }

    public static async Task<HttpResponseMessage> RegisterDispatchAsync(HttpClient client, Guid handoverId, string expectedDeliveryFromDate = "2026-10-01")
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync(
            $"/api/equipment-handovers/{handoverId}/dispatch", new { ExpectedDeliveryFromDate = expectedDeliveryFromDate }, csrfToken);
    }

    public static async Task<HttpResponseMessage> AddItemAsync(HttpClient client, Guid handoverId, Guid equipmentId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync($"/api/equipment-handovers/{handoverId}/items", new { EquipmentId = equipmentId }, csrfToken);
    }

    public static async Task<HttpResponseMessage> RecordInspectionAsync(
        HttpClient client, Guid handoverId, Guid itemId, string condition = "Good", string? notes = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PutWithCsrfAsync(
            $"/api/equipment-handovers/{handoverId}/items/{itemId}/inspection", new { Condition = condition, Notes = notes }, csrfToken);
    }

    public static async Task<HttpResponseMessage> UploadPhotoAsync(
        HttpClient client, Guid handoverId, Guid itemId, byte[]? bytes = null, string fileName = "photo.jpg", string contentType = "image/jpeg")
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostFileWithCsrfAsync(
            $"/api/equipment-handovers/{handoverId}/items/{itemId}/inspection/photos", "file", bytes ?? MinimalJpegBytes, fileName, contentType, csrfToken);
    }

    public static async Task<HttpResponseMessage> SetDeliveredOnAsync(HttpClient client, Guid handoverId, DateOnly deliveredOn)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PutWithCsrfAsync($"/api/equipment-handovers/{handoverId}", new { DeliveredOn = deliveredOn, Notes = (string?)null }, csrfToken);
    }

    public static async Task<HttpResponseMessage> FinalizeAsync(HttpClient client, Guid handoverId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handoverId}/finalize", null, csrfToken);
    }

    /// <summary>Full happy-path setup: draft handover, one item, inspection, and (if <paramref name="photoCount"/> &gt; 0) that many valid photos — stops just short of finalizing.</summary>
    public static async Task<(EquipmentHandoverDetailResponse Handover, Guid ItemId)> PrepareReadyToFinalizeAsync(
        HttpClient client, Guid consultantId, Guid equipmentId, int photoCount, string condition = "Good", string deliveryMethod = "InPerson")
    {
        var handover = await CreateDraftHandoverAsync(client, consultantId, deliveryMethod: deliveryMethod);
        var addResponse = await AddItemAsync(client, handover.Id, equipmentId);
        addResponse.EnsureSuccessStatusCode();
        var withItem = (await addResponse.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        var itemId = withItem.Items.Single().Id;

        (await RecordInspectionAsync(client, handover.Id, itemId, condition)).EnsureSuccessStatusCode();

        for (var i = 0; i < photoCount; i++)
        {
            (await UploadPhotoAsync(client, handover.Id, itemId)).EnsureSuccessStatusCode();
        }

        (await SetDeliveredOnAsync(client, handover.Id, DateOnly.FromDateTime(DateTime.UtcNow))).EnsureSuccessStatusCode();

        return (withItem, itemId);
    }
}
