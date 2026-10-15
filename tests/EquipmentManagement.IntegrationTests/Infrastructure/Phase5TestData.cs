using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.Returns;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Drives the return/return-inspection/damage/maintenance workflow through the
/// real HTTP API for integration test setup — same rationale as <see cref="Phase4TestData"/>.
/// </summary>
public static class Phase5TestData
{
    public static async Task<HttpResponseMessage> InitiateReturnAsync(
        HttpClient client, Guid assignmentId, DateOnly? expectedReturnDate = null, string? reason = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync(
            $"/api/equipment-assignments/{assignmentId}/return", new { ExpectedReturnDate = expectedReturnDate, Reason = reason }, csrfToken);
    }

    public static async Task<EquipmentReturnDetailResponse> InitiateReturnSuccessfullyAsync(HttpClient client, Guid assignmentId)
    {
        var response = await InitiateReturnAsync(client, assignmentId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentReturnDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    /// <summary><paramref name="photoCount"/> defaults to 0 (Generic equipment, which requires none); pass 3 for Laptop/MobilePhone equipment.</summary>
    public static async Task<HttpResponseMessage> ReceiveReturnAsync(
        HttpClient client, Guid returnId, DateOnly receivedOn, Guid receivedByUserId, int photoCount = 0)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var photos = Enumerable.Range(0, photoCount).Select(_ => (Phase3TestData.MinimalJpegBytes, "condition.jpg", "image/jpeg")).ToList();
        return await client.PostMultipartWithCsrfAsync(
            $"/api/equipment-returns/{returnId}/receive", "Photos", photos,
            new Dictionary<string, string> { ["ReceivedOn"] = receivedOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ["ReceivedByUserId"] = receivedByUserId.ToString() },
            csrfToken);
    }

    public static async Task<HttpResponseMessage> RecordReturnInspectionAsync(
        HttpClient client, Guid returnId, string condition = "Good", string? notes = null, bool hasDamage = false, string? damageDescription = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PutWithCsrfAsync(
            $"/api/equipment-returns/{returnId}/inspection",
            new { Condition = condition, Notes = notes, HasDamage = hasDamage, DamageDescription = damageDescription }, csrfToken);
    }

    public static async Task<HttpResponseMessage> UploadReturnPhotoAsync(
        HttpClient client, Guid returnId, byte[]? bytes = null, string fileName = "return.jpg", string contentType = "image/jpeg")
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostFileWithCsrfAsync(
            $"/api/equipment-returns/{returnId}/inspection/photos", "file", bytes ?? Phase4TestData.MinimalJpegBytes, fileName, contentType, csrfToken);
    }

    public static async Task<HttpResponseMessage> UploadDamagePhotoAsync(
        HttpClient client, Guid returnId, byte[]? bytes = null, string fileName = "damage.jpg", string contentType = "image/jpeg")
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostFileWithCsrfAsync(
            $"/api/equipment-returns/{returnId}/damage/photos", "file", bytes ?? Phase4TestData.MinimalJpegBytes, fileName, contentType, csrfToken);
    }

    public static async Task<HttpResponseMessage> CancelReturnAsync(HttpClient client, Guid returnId, string? reason = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync($"/api/equipment-returns/{returnId}/cancel", new { Reason = reason }, csrfToken);
    }

    public static async Task<HttpResponseMessage> FinalizeReturnInspectionAsync(HttpClient client, Guid returnId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync<object?>($"/api/equipment-returns/{returnId}/inspection/finalize", null, csrfToken);
    }

    public static async Task<HttpResponseMessage> StartMaintenanceAsync(HttpClient client, Guid equipmentId, string? notes = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync($"/api/equipment/{equipmentId}/maintenance", new { Notes = notes }, csrfToken);
    }

    public static async Task<HttpResponseMessage> CompleteMaintenanceAsync(
        HttpClient client, Guid maintenanceId, string outcome, string? notes = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync($"/api/maintenance/{maintenanceId}/complete", new { Outcome = outcome, Notes = notes }, csrfToken);
    }

    /// <summary>Drives a Generic equipment item all the way to <c>Damaged</c> through the return workflow, since that's the only path that produces one.</summary>
    public static async Task<Guid> DamageAGenericEquipmentAsync(HttpClient client, ApiWebApplicationFactory factory)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);

        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var assignmentId = await GetActiveAssignmentIdAsync(client, equipment.Id);
        var equipmentReturn = await InitiateReturnSuccessfullyAsync(client, assignmentId);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua Receiver {Guid.NewGuid():N}, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await ReceiveReturnAsync(client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId)).EnsureSuccessStatusCode();
        (await RecordReturnInspectionAsync(client, equipmentReturn.Id, hasDamage: true, damageDescription: "Broken casing"))
            .EnsureSuccessStatusCode();
        (await UploadDamagePhotoAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
        (await FinalizeReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();

        return equipment.Id;
    }

    /// <summary>The sole Active assignment for an equipment — used after finalizing a handover, since the finalize response doesn't echo the assignment id directly.</summary>
    public static async Task<Guid> GetActiveAssignmentIdAsync(HttpClient client, Guid equipmentId)
    {
        var response = await client.GetAsync($"/api/equipment-assignments?equipmentId={equipmentId}&status=Active");
        response.EnsureSuccessStatusCode();
        var page = (await response.Content.ReadFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(HttpClientTestExtensions.JsonOptions))!;
        return page.Items.Single().Id;
    }

    /// <summary>
    /// Test-only: directly resets an already-Ended assignment back to Active
    /// and its equipment back to Assigned, bypassing every application-level
    /// rule — simulates a hypothetical future bug or manual data correction
    /// that reactivates an assignment. Used only to prove that the database's
    /// own unique index on EquipmentReturns(EquipmentAssignmentId) — not the
    /// application's `assignment.Status != Active` check — is what makes a
    /// second EquipmentReturn for the same assignment structurally
    /// impossible, even when the application-level guard is bypassed.
    /// </summary>
    public static async Task ForceAssignmentReactivatableStateAsync(ApiWebApplicationFactory factory, Guid assignmentId, Guid equipmentId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var assignment = await dbContext.EquipmentAssignments.FirstAsync(a => a.Id == assignmentId);
        assignment.Status = AssignmentStatus.Active;
        assignment.EndedAtUtc = null;

        var equipment = await dbContext.Equipment.FirstAsync(e => e.Id == equipmentId);
        equipment.Status = EquipmentStatus.Assigned;

        await dbContext.SaveChangesAsync();
    }
}
