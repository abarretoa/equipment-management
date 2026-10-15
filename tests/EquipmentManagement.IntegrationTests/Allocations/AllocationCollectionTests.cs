using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.Allocations;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Allocations;

/// <summary>
/// Ending a <c>ConsultantAllocation</c> is not always offboarding — a
/// Consultant can end one allocation while continuing on another. The
/// equipment-collection notification workflow must run only when HR
/// explicitly flags the end as end-of-engagement via <c>IsEndOfEngagement</c>,
/// never merely because an <c>EndDate</c> was supplied, and it must never
/// end <c>EquipmentAssignment</c>/custody in any case. The returns an end of
/// engagement initiates are covered by <see cref="EndOfEngagementReturnTests"/>.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class AllocationCollectionTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task EndAllocation_WithIsEndOfEngagementOmitted_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var allocation = await CreateActiveAllocationAsync(client, consultantId);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync(
            $"/api/consultants/{consultantId}/allocations/{allocation.Id}/end", new { EndDate = "2026-10-15" }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EndAllocation_WithIsEndOfEngagementExplicitlyNull_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var allocation = await CreateActiveAllocationAsync(client, consultantId);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync(
            $"/api/consultants/{consultantId}/allocations/{allocation.Id}/end",
            new { EndDate = "2026-10-15", IsEndOfEngagement = (bool?)null }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NotEndOfEngagement_WithActiveEquipment_EndsNormally_NoEstimateRequired_NoEmail()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, allocationId, equipmentId, consultantEmail) = await CreateConsultantWithAllocationAndEquipmentAsync(client);
        GetEmailSender().Reset();

        var response = await EndAllocationAsync(client, consultantId, allocationId, isEndOfEngagement: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ended = (await response.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal("Ended", ended.Status.ToString());
        Assert.False(ended.IsEndOfEngagement);
        Assert.Null(ended.ExpectedCollectionFromDate);

        Assert.DoesNotContain(GetEmailSender().SentMessages, m => m.ToAddress == consultantEmail);
        await AssertCustodyUnchangedAsync(client, equipmentId);
    }

    [Fact]
    public async Task EndOfEngagement_WithNoActiveEquipment_EndsNormally_NoEmail()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var allocation = await CreateActiveAllocationAsync(client, consultantId);
        GetEmailSender().Reset();

        var response = await EndAllocationAsync(client, consultantId, allocation.Id, isEndOfEngagement: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ended = (await response.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.True(ended.IsEndOfEngagement);
        Assert.Empty(GetEmailSender().SentMessages);
    }

    [Fact]
    public async Task EndOfEngagement_WithActiveEquipment_WithoutCollectionEstimate_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, allocationId, _, _) = await CreateConsultantWithAllocationAndEquipmentAsync(client);

        var response = await EndAllocationAsync(client, consultantId, allocationId, isEndOfEngagement: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EndOfEngagement_WithActiveEquipment_WithCollectionEstimate_EndsAndSendsOneEmail_CustodyStaysWithConsultant()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, allocationId, equipmentId, consultantEmail) = await CreateConsultantWithAllocationAndEquipmentAsync(client);
        GetEmailSender().Reset();

        var response = await EndAllocationAsync(
            client, consultantId, allocationId, isEndOfEngagement: true, expectedCollectionFromDate: "2026-11-01", expectedCollectionToDate: "2026-11-05");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ended = (await response.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.True(ended.IsEndOfEngagement);
        Assert.Equal(new DateOnly(2026, 11, 1), ended.ExpectedCollectionFromDate);
        Assert.Equal(new DateOnly(2026, 11, 5), ended.ExpectedCollectionToDate);
        Assert.Equal("Ended", ended.Status.ToString());

        var sent = Assert.Single(GetEmailSender().SentMessages, m => m.ToAddress == consultantEmail);
        Assert.Null(sent.Attachment);
        Assert.True(sent.IncludeSignature);

        // Iteration 4: the return is initiated, but custody only
        // ends at physical receipt (see EndOfEngagementReturnTests).
        var equipment = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(equipment!.CurrentCustody);
        Assert.Equal(EquipmentStatus.ReturnPending, equipment.Status);
    }

    private RecordingEmailSender GetEmailSender() => factory.Services.GetRequiredService<RecordingEmailSender>();

    private static async Task AssertCustodyUnchangedAsync(HttpClient client, Guid equipmentId)
    {
        var equipment = await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions);
        Assert.NotNull(equipment!.CurrentCustody);
        Assert.Equal(EquipmentStatus.Assigned, equipment.Status);
    }

    private static Task<HttpResponseMessage> EndAllocationAsync(
        HttpClient client, Guid consultantId, Guid allocationId, bool isEndOfEngagement,
        string? expectedCollectionFromDate = null, string? expectedCollectionToDate = null) =>
        EndAllocationInternalAsync(client, consultantId, allocationId, isEndOfEngagement, expectedCollectionFromDate, expectedCollectionToDate);

    private static async Task<HttpResponseMessage> EndAllocationInternalAsync(
        HttpClient client, Guid consultantId, Guid allocationId, bool isEndOfEngagement,
        string? expectedCollectionFromDate, string? expectedCollectionToDate)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync(
            $"/api/consultants/{consultantId}/allocations/{allocationId}/end",
            new
            {
                EndDate = "2026-10-15",
                IsEndOfEngagement = isEndOfEngagement,
                ExpectedCollectionFromDate = expectedCollectionFromDate,
                ExpectedCollectionToDate = expectedCollectionToDate,
            },
            csrfToken);
    }

    private static async Task<ConsultantAllocationResponse> CreateActiveAllocationAsync(HttpClient client, Guid consultantId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = "Internal",
            EngagementType = "EmploymentContract",
            RoleOrFunction = "Backend Developer",
            StartDate = "2026-01-01",
        }, csrfToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<(Guid ConsultantId, Guid AllocationId, Guid EquipmentId, string ConsultantEmail)> CreateConsultantWithAllocationAndEquipmentAsync(
        HttpClient client)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var consultant = await client.GetFromJsonAsync<EquipmentManagement.Api.Contracts.Consultants.ConsultantDetailResponse>(
            $"/api/consultants/{consultantId}", HttpClientTestExtensions.JsonOptions);
        var allocation = await CreateActiveAllocationAsync(client, consultantId);

        var equipmentType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, equipmentType.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        return (consultantId, allocation.Id, equipment.Id, consultant!.Email);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
