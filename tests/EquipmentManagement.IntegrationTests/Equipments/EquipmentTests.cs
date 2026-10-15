using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.Maintenance;
using EquipmentManagement.Api.Contracts.Returns;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Equipments;

/// <summary>
/// Physical-asset registry rules against a real PostgreSQL database
/// (docs/PROJECT_SPEC.md, section 7; Phase 3 scope).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task CreateGenericEquipment_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");

        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        Assert.Equal("Available", equipment.Status.ToString());
        Assert.StartsWith(type.AssetTagPrefix + "-", equipment.AssetTag, StringComparison.Ordinal);
        Assert.Null(equipment.LaptopSpecification);
    }

    [Fact]
    public async Task CreateLaptopWithFullSpecification_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, $"Laptop {Guid.NewGuid():N}", "Laptop", "LT");

        var equipment = await Phase3TestData.CreateLaptopEquipmentAsync(client, type.Id);

        Assert.NotNull(equipment.LaptopSpecification);
        Assert.Equal("Intel i7-1365U", equipment.LaptopSpecification!.Processor);
        Assert.Equal("Dell", equipment.Manufacturer);
    }

    [Fact]
    public async Task LaptopMissingSpecification_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, $"Laptop2 {Guid.NewGuid():N}", "Laptop", "LB");
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/equipment", new
        {
            EquipmentTypeId = type.Id,
            Manufacturer = "Dell",
            SerialNumber = $"SN{Guid.NewGuid():N}"[..12],
        }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LaptopMissingManufacturerOrSerialNumber_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, $"Laptop3 {Guid.NewGuid():N}", "Laptop", "LC");
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/equipment", new
        {
            EquipmentTypeId = type.Id,
            LaptopSpecification = new { Processor = "Intel i7", Memory = "16 GB", Storage = "512 GB SSD" },
        }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GenericEquipmentWithLaptopSpecification_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/equipment", new
        {
            EquipmentTypeId = type.Id,
            Manufacturer = "Logitech",
            LaptopSpecification = new { Processor = "Intel i7", Memory = "16 GB", Storage = "512 GB SSD" },
        }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateSerialNumber_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var serial = $"SN{Guid.NewGuid():N}"[..12];
        await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id, serial);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        // Same serial, different case/spacing — must still collide after normalization.
        var response = await client.PostMultipartWithCsrfAsync(
            "/api/equipment", "Photos", [],
            new Dictionary<string, string>
            {
                ["EquipmentTypeId"] = type.Id.ToString(),
                ["Manufacturer"] = "Logitech",
                ["SerialNumber"] = $"  {serial.ToLowerInvariant()}  ",
            }, csrfToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateSerialNumber_ConcurrentCreate_OnlyOneSucceeds()
    {
        // Regression test: EquipmentService's duplicate-serial-number catch
        // used to match only DbUpdateException with a directly-inner
        // PostgresException, and did not distinguish which of Equipment's two
        // unique constraints (AssetTag vs. NormalizedSerialNumber) actually
        // fired. Two real, simultaneous inserts race the serial-number index.
        using var clientA = await LoginAsNewAdministratorAsync();
        using var clientB = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(clientA, kind: "Generic");
        var serial = $"SN{Guid.NewGuid():N}"[..12];

        var csrfA = await clientA.GetAntiforgeryTokenAsync();
        var csrfB = await clientB.GetAntiforgeryTokenAsync();

        Dictionary<string, string> NewEquipmentPayload() => new()
        {
            ["EquipmentTypeId"] = type.Id.ToString(),
            ["Manufacturer"] = "Logitech",
            ["SerialNumber"] = serial,
        };

        var results = await Task.WhenAll(
            clientA.PostMultipartWithCsrfAsync("/api/equipment", "Photos", [], NewEquipmentPayload(), csrfA),
            clientB.PostMultipartWithCsrfAsync("/api/equipment", "Photos", [], NewEquipmentPayload(), csrfB));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task NullSerialNumber_IsAllowedForGenericEquipment()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");

        var first = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id, serialNumber: null);
        var second = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id, serialNumber: null);

        Assert.Null(first.SerialNumber);
        Assert.Null(second.SerialNumber);
        Assert.NotEqual(first.AssetTag, second.AssetTag);
    }

    [Fact]
    public async Task ConcurrentEquipmentCreation_NeverGeneratesDuplicateAssetTags()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Phase3TestData.CreateGenericEquipmentAsync(client, type.Id))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var assetTags = results.Select(r => r.AssetTag).ToList();
        Assert.Equal(assetTags.Count, assetTags.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task GenericUpdate_CannotChangeEquipmentTypeAssetTagOrStatus()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        // The request contract has no field for EquipmentTypeId/AssetTag/Status
        // at all — sending them anyway must have no effect (ASP.NET Core model
        // binding simply ignores unknown JSON properties).
        var response = await client.PutWithCsrfAsync($"/api/equipment/{equipment.Id}", new
        {
            Manufacturer = "Updated Manufacturer",
            EquipmentTypeId = Guid.NewGuid(),
            AssetTag = "HACKED-0001",
            Status = "Retired",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions);

        Assert.Equal(type.Id, updated!.EquipmentTypeId);
        Assert.Equal(equipment.AssetTag, updated.AssetTag);
        Assert.Equal("Available", updated.Status.ToString());
        Assert.Equal("Updated Manufacturer", updated.Manufacturer);
    }

    [Fact]
    public async Task Retire_FromAvailable_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var retired = await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Retired", retired!.Status.ToString());
    }

    [Fact]
    public async Task RetiringAnAlreadyRetiredEquipment_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var firstCsrf = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, firstCsrf);

        var secondCsrf = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, secondCsrf);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Hr_CannotRetireEquipment()
    {
        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR EQ3, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(hrClient, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(hrClient, type.Id);

        var csrfToken = await hrClient.GetAntiforgeryTokenAsync();
        var response = await hrClient.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, csrfToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Operational case: a damaged/non-repairable item must be permanently
    /// removable from service, even though it never returned to <c>Available</c>.
    /// </summary>
    [Fact]
    public async Task Retire_FromDamaged_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var equipmentId = await Phase5TestData.DamageAGenericEquipmentAsync(client, factory);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipmentId}/retire", null, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var retired = await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Equal("Retired", retired!.Status.ToString());
    }

    [Fact]
    public async Task Hr_CannotRetireDamagedEquipment()
    {
        using var adminClient = await LoginAsNewAdministratorAsync();
        var equipmentId = await Phase5TestData.DamageAGenericEquipmentAsync(adminClient, factory);

        var (hrEmail, hrPassword) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua HR EQ4, Lisboa");
        using var hrClient = factory.CreateClient();
        await hrClient.LoginAsync(hrEmail, hrPassword);

        var csrfToken = await hrClient.GetAntiforgeryTokenAsync();
        var response = await hrClient.PostWithCsrfAsync<object?>($"/api/equipment/{equipmentId}/retire", null, csrfToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Retire_WhileAssigned_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, csrfToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Retire_WhileReturnPending_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();
        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipment.Id);
        await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, csrfToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Retire_WhileUnderInspection_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();
        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipment.Id);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);

        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua Receiver {Guid.NewGuid():N}, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId))
            .EnsureSuccessStatusCode();

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipment.Id}/retire", null, csrfToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Retire_WhileUnderMaintenance_IsRejected()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var equipmentId = await Phase5TestData.DamageAGenericEquipmentAsync(client, factory);
        (await Phase5TestData.StartMaintenanceAsync(client, equipmentId)).EnsureSuccessStatusCode();

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipmentId}/retire", null, csrfToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// Retiring must be a pure status flip: the equipment's return, damage and
    /// maintenance trail — and the assignment it once had — must remain exactly
    /// as they were, including the damage record staying <c>Open</c> (retiring
    /// is not a way to "resolve" damage).
    /// </summary>
    [Fact]
    public async Task Retire_FromDamaged_PreservesReturnDamageAndMaintenanceHistory()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var equipmentId = await Phase5TestData.DamageAGenericEquipmentAsync(client, factory);

        var startResponse = await Phase5TestData.StartMaintenanceAsync(client, equipmentId);
        startResponse.EnsureSuccessStatusCode();
        var maintenance = (await startResponse.Content.ReadFromJsonAsync<MaintenanceDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        (await Phase5TestData.CompleteMaintenanceAsync(client, maintenance.Id, "NotRepairable")).EnsureSuccessStatusCode();

        var returnsBefore = await client.GetFromJsonAsync<PagedResponse<EquipmentReturnSummaryResponse>>(
            $"/api/equipment-returns?equipmentId={equipmentId}", HttpClientTestExtensions.JsonOptions);
        var maintenanceBefore = await client.GetFromJsonAsync<PagedResponse<MaintenanceSummaryResponse>>(
            $"/api/maintenance?equipmentId={equipmentId}", HttpClientTestExtensions.JsonOptions);
        var assignmentsBefore = await client.GetFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(
            $"/api/equipment-assignments?equipmentId={equipmentId}", HttpClientTestExtensions.JsonOptions);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var retireResponse = await client.PostWithCsrfAsync<object?>($"/api/equipment/{equipmentId}/retire", null, csrfToken);
        Assert.Equal(HttpStatusCode.OK, retireResponse.StatusCode);

        var returnsAfter = await client.GetFromJsonAsync<PagedResponse<EquipmentReturnSummaryResponse>>(
            $"/api/equipment-returns?equipmentId={equipmentId}", HttpClientTestExtensions.JsonOptions);
        var maintenanceAfter = await client.GetFromJsonAsync<PagedResponse<MaintenanceSummaryResponse>>(
            $"/api/maintenance?equipmentId={equipmentId}", HttpClientTestExtensions.JsonOptions);
        var assignmentsAfter = await client.GetFromJsonAsync<PagedResponse<EquipmentAssignmentResponse>>(
            $"/api/equipment-assignments?equipmentId={equipmentId}", HttpClientTestExtensions.JsonOptions);

        Assert.Equal(returnsBefore!.Items.Select(r => r.Id), returnsAfter!.Items.Select(r => r.Id));
        Assert.Equal(maintenanceBefore!.Items.Select(m => m.Id), maintenanceAfter!.Items.Select(m => m.Id));
        Assert.Equal(
            assignmentsBefore!.Items.Select(a => (a.Id, a.Status.ToString())),
            assignmentsAfter!.Items.Select(a => (a.Id, a.Status.ToString())));

        var returnDetail = await client.GetFromJsonAsync<EquipmentReturnDetailResponse>(
            $"/api/equipment-returns/{returnsAfter.Items.Single().Id}", HttpClientTestExtensions.JsonOptions);
        var damage = returnDetail!.Inspection?.Damage;
        Assert.NotNull(damage);
        Assert.Equal("Broken casing", damage!.Description);
        Assert.Equal("Open", damage.Status.ToString());
    }

    [Fact]
    public async Task EquipmentListSummary_OmitsNotesAndLaptopSpecification()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, $"Laptop4 {Guid.NewGuid():N}", "Laptop", "LD");
        await Phase3TestData.CreateLaptopEquipmentAsync(client, type.Id);

        var response = await client.GetAsync("/api/equipment?pageSize=200");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("laptopSpecification", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("processor", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStats_ReturnsCountsByStatusAndAvailableByType()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, $"StatsType {Guid.NewGuid():N}", "Generic", "ST");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var response = await client.GetAsync("/api/equipment/stats");
        response.EnsureSuccessStatusCode();
        var stats = (await response.Content.ReadFromJsonAsync<EquipmentStatsResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.True(stats.TotalCount > 0);
        Assert.Contains(stats.ByStatus, s => s.Status == equipment.Status && s.Count > 0);
        Assert.Contains(stats.AvailableByType, t => t.EquipmentTypeId == type.Id && t.Count > 0);
    }

    [Fact]
    public async Task GetStats_HrCanReadIt()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "HR");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);

        var response = await client.GetAsync("/api/equipment/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Iteration 1 — the dashboard's Equipment-Type
    /// click filter. Two distinct types, one retired item mixed in to prove
    /// the filter scopes ByStatus too, not just the Available breakdown.
    /// </summary>
    [Fact]
    public async Task GetStats_WithEquipmentTypeId_ReturnsOnlyThatTypesCounts()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var typeA = await Phase3TestData.CreateEquipmentTypeAsync(client, $"StatsFilterA {Guid.NewGuid():N}", "Generic", "FA");
        var typeB = await Phase3TestData.CreateEquipmentTypeAsync(client, $"StatsFilterB {Guid.NewGuid():N}", "Generic", "FB");

        await Phase3TestData.CreateGenericEquipmentAsync(client, typeA.Id);
        await Phase3TestData.CreateGenericEquipmentAsync(client, typeA.Id);
        var retiredA = await Phase3TestData.CreateGenericEquipmentAsync(client, typeA.Id);
        var retireCsrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync<object?>($"/api/equipment/{retiredA.Id}/retire", null, retireCsrf)).EnsureSuccessStatusCode();

        await Phase3TestData.CreateGenericEquipmentAsync(client, typeB.Id);

        var response = await client.GetAsync($"/api/equipment/stats?equipmentTypeId={typeA.Id}");
        response.EnsureSuccessStatusCode();
        var stats = (await response.Content.ReadFromJsonAsync<EquipmentStatsResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(3, stats.TotalCount);
        Assert.Equal(2, stats.ByStatus.Single(s => s.Status == EquipmentStatus.Available).Count);
        Assert.Equal(1, stats.ByStatus.Single(s => s.Status == EquipmentStatus.Retired).Count);
        Assert.DoesNotContain(stats.ByStatus, s => s.Status != EquipmentStatus.Available && s.Status != EquipmentStatus.Retired && s.Count > 0);
        Assert.All(stats.AvailableByType, t => Assert.Equal(typeA.Id, t.EquipmentTypeId));
    }

    /// <summary>Unfiltered stats must remain the full aggregate — the type filter is opt-in, never a default narrowing.</summary>
    [Fact]
    public async Task GetStats_WithoutEquipmentTypeId_IncludesEveryType()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var typeA = await Phase3TestData.CreateEquipmentTypeAsync(client, $"StatsAllA {Guid.NewGuid():N}", "Generic", "AA");
        var typeB = await Phase3TestData.CreateEquipmentTypeAsync(client, $"StatsAllB {Guid.NewGuid():N}", "Generic", "AB");
        await Phase3TestData.CreateGenericEquipmentAsync(client, typeA.Id);
        await Phase3TestData.CreateGenericEquipmentAsync(client, typeB.Id);

        var response = await client.GetAsync("/api/equipment/stats");
        response.EnsureSuccessStatusCode();
        var stats = (await response.Content.ReadFromJsonAsync<EquipmentStatsResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Contains(stats.AvailableByType, t => t.EquipmentTypeId == typeA.Id);
        Assert.Contains(stats.AvailableByType, t => t.EquipmentTypeId == typeB.Id);
    }

    /// <summary>A type id matching no EquipmentType is a sensible empty aggregate (zero counts), not a 404 — this is a stats query, not a single-resource lookup.</summary>
    [Fact]
    public async Task GetStats_WithNonexistentEquipmentTypeId_ReturnsZeroCounts()
    {
        using var client = await LoginAsNewAdministratorAsync();

        var response = await client.GetAsync($"/api/equipment/stats?equipmentTypeId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stats = (await response.Content.ReadFromJsonAsync<EquipmentStatsResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(0, stats.TotalCount);
        Assert.Empty(stats.ByStatus);
        Assert.Empty(stats.AvailableByType);
    }

    /// <summary>Stats must count the complete matching dataset via SQL aggregation, never a paginated in-memory subset — create more than one list page's worth and confirm the full count.</summary>
    [Fact]
    public async Task GetStats_CountsCompleteDatasetNotOnePage()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, $"StatsBulk {Guid.NewGuid():N}", "Generic", "SB");

        const int equipmentCount = 25; // exceeds the default list pageSize of 20
        for (var i = 0; i < equipmentCount; i++)
        {
            await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        }

        var response = await client.GetAsync($"/api/equipment/stats?equipmentTypeId={type.Id}");
        response.EnsureSuccessStatusCode();
        var stats = (await response.Content.ReadFromJsonAsync<EquipmentStatsResponse>(HttpClientTestExtensions.JsonOptions))!;

        Assert.Equal(equipmentCount, stats.TotalCount);
        Assert.Equal(equipmentCount, stats.ByStatus.Single(s => s.Status == EquipmentStatus.Available).Count);
    }

    /// <summary>Iteration 1 — optional purchase metadata on create.</summary>
    [Fact]
    public async Task CreateEquipment_WithPurchaseInformation_Persists()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostMultipartWithCsrfAsync(
            "/api/equipment", "Photos", [],
            new Dictionary<string, string>
            {
                ["EquipmentTypeId"] = type.Id.ToString(),
                ["Manufacturer"] = "Logitech",
                ["PurchaseDate"] = "2026-01-15",
                ["InvoiceNumber"] = "INV-2026-0042",
                ["PurchaseVendor"] = "Worten",
            }, csrfToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(new DateOnly(2026, 1, 15), created.PurchaseDate);
        Assert.Equal("INV-2026-0042", created.InvoiceNumber);
        Assert.Equal("Worten", created.PurchaseVendor);
    }

    /// <summary>Purchase fields must remain fully optional — existing/new Equipment without them stays valid.</summary>
    [Fact]
    public async Task CreateEquipment_WithoutPurchaseInformation_LeavesFieldsNull()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");

        var created = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        Assert.Null(created.PurchaseDate);
        Assert.Null(created.InvoiceNumber);
        Assert.Null(created.PurchaseVendor);
    }

    [Fact]
    public async Task UpdateEquipment_CanSetAndThenClearPurchaseInformation()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        var setCsrf = await client.GetAntiforgeryTokenAsync();
        var setResponse = await client.PutWithCsrfAsync($"/api/equipment/{equipment.Id}", new
        {
            Manufacturer = equipment.Manufacturer,
            PurchaseDate = "2026-02-01",
            InvoiceNumber = "INV-100",
            PurchaseVendor = "Fnac",
        }, setCsrf);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);
        var withPurchase = (await setResponse.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Equal(new DateOnly(2026, 2, 1), withPurchase.PurchaseDate);
        Assert.Equal("INV-100", withPurchase.InvoiceNumber);
        Assert.Equal("Fnac", withPurchase.PurchaseVendor);

        // Existing update semantics: omitting a field in the request clears it (same as Manufacturer/Model/Notes today).
        var clearCsrf = await client.GetAntiforgeryTokenAsync();
        var clearResponse = await client.PutWithCsrfAsync($"/api/equipment/{equipment.Id}", new
        {
            Manufacturer = equipment.Manufacturer,
        }, clearCsrf);
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        var cleared = (await clearResponse.Content.ReadFromJsonAsync<EquipmentDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        Assert.Null(cleared.PurchaseDate);
        Assert.Null(cleared.InvoiceNumber);
        Assert.Null(cleared.PurchaseVendor);
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
