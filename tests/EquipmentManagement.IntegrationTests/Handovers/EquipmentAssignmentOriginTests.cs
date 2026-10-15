using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Handovers;

/// <summary>
/// <see cref="EquipmentAssignment.Origin"/>: a finalized Handover still
/// produces a Handover-origin assignment referencing it, and the database
/// CHECK constraint refuses any assignment whose Handover references do not
/// match its origin.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class EquipmentAssignmentOriginTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task FinalizedHandover_CreatesHandoverOriginAssignmentWithReferences()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        await Phase4TestData.EnsureEligibleAllocationAsync(client, consultantId);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var (handover, itemId) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);
        (await Phase4TestData.FinalizeAsync(client, handover.Id)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var assignment = await db.EquipmentAssignments.AsNoTracking().SingleAsync(a => a.EquipmentId == equipment.Id);

        Assert.Equal(EquipmentAssignmentOrigin.Handover, assignment.Origin);
        Assert.Equal(handover.Id, assignment.EquipmentHandoverId);
        Assert.Equal(itemId, assignment.EquipmentHandoverItemId);
    }

    [Fact]
    public async Task Database_RejectsHandoverOriginWithoutHandoverReferences()
    {
        var (consultantId, equipmentId) = await CreateConsultantAndEquipmentAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() => InsertAssignmentAsync(
            consultantId, equipmentId, EquipmentAssignmentOrigin.Handover, handoverId: null, handoverItemId: null));
    }

    [Fact]
    public async Task Database_RejectsCurrentInventoryImportOriginWithHandoverReferences()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        await Phase4TestData.EnsureEligibleAllocationAsync(client, consultantId);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var delivered = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var (handover, itemId) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, delivered.Id, photoCount: 0);
        var other = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);

        // Real Handover/HandoverItem ids, so only the CHECK constraint can object.
        await Assert.ThrowsAsync<DbUpdateException>(() => InsertAssignmentAsync(
            consultantId, other.Id, EquipmentAssignmentOrigin.CurrentInventoryImport, handover.Id, itemId));
    }

    [Fact]
    public async Task Database_AcceptsCurrentInventoryImportOriginWithoutHandoverReferences()
    {
        var (consultantId, equipmentId) = await CreateConsultantAndEquipmentAsync();

        await InsertAssignmentAsync(consultantId, equipmentId, EquipmentAssignmentOrigin.CurrentInventoryImport, handoverId: null, handoverItemId: null);
    }

    private async Task<(Guid ConsultantId, Guid EquipmentId)> CreateConsultantAndEquipmentAsync()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic");
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        return (consultantId, equipment.Id);
    }

    private async Task InsertAssignmentAsync(
        Guid consultantId, Guid equipmentId, EquipmentAssignmentOrigin origin, Guid? handoverId, Guid? handoverItemId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        db.EquipmentAssignments.Add(new EquipmentAssignment
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            ConsultantId = consultantId,
            Origin = origin,
            EquipmentHandoverId = handoverId,
            EquipmentHandoverItemId = handoverItemId,
            Status = AssignmentStatus.Ended,
            StartedAtUtc = now,
            EndedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
