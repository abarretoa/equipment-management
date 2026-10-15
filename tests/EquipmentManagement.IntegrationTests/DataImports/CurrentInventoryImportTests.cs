using System.Net;
using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts.DataImports;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.DataImports;

/// <summary>
/// Current Inventory import against real PostgreSQL: Consultant +
/// Allocation + Equipment + current custody from one workbook, for both
/// Regime = Interno and Externo, with every create/reuse/conflict rule, the
/// stale-preview re-validation, atomicity, and the "no fake workflow" guarantees.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class CurrentInventoryImportTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    // ---------------------------------------------------------------- B / C

    [Fact]
    public async Task NewExternalConsultant_CreatesConsultantAllocationEquipmentAndAssignment()
    {
        using var client = await LoginAsAsync("Administration");
        var (companyId, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var laptopType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LT");
        var person = InventoryPerson.NewExternal(companyName);
        var row = InventoryRow.Laptop(laptopType.Name, person);

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([row]));
        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal(1, preview.Summary.NewConsultants);
        Assert.Equal(1, preview.Summary.NewAllocations);
        Assert.Equal(1, preview.Summary.EquipmentToAssign);
        Assert.Equal(1, preview.Summary.ExternalConsultants);
        Assert.Equal(CurrentInventoryConsultantAction.Create, preview.Rows[0].ConsultantAction);
        Assert.Equal(WorkforceType.External, preview.Rows[0].WorkforceType);

        var confirm = await ConfirmAsync(client, preview.ImportSessionId);
        Assert.Equal(1, confirm.CreatedConsultants);
        Assert.Equal(1, confirm.CreatedAllocations);
        Assert.Equal(1, confirm.CreatedEquipment);
        Assert.Equal(1, confirm.CreatedAssignments);

        var state = await LoadPersonStateAsync(person.Nif);
        var allocation = Assert.Single(state.Allocations);
        Assert.Equal(WorkforceType.External, allocation.WorkforceType);
        Assert.Equal(EngagementType.ServiceProvider, allocation.EngagementType);
        Assert.Equal(companyId, allocation.CompanyId);
        Assert.Equal(AllocationStatus.Active, allocation.Status);

        var assignment = Assert.Single(state.Assignments);
        Assert.Equal(AssignmentStatus.Active, assignment.Status);
        Assert.Equal(EquipmentAssignmentOrigin.CurrentInventoryImport, assignment.Origin);
        Assert.Null(assignment.EquipmentHandoverId);
        Assert.Null(assignment.EquipmentHandoverItemId);

        var equipment = await GetEquipmentAsync(client, assignment.EquipmentId);
        Assert.Equal(EquipmentStatus.Assigned, equipment.Status);
        Assert.Equal(state.Consultant!.Id, equipment.CurrentCustody!.ConsultantId);
        Assert.Null(equipment.CurrentCustody.EquipmentHandoverId);
        Assert.Equal(EquipmentAssignmentOrigin.CurrentInventoryImport, equipment.CurrentCustody.Origin);
    }

    [Fact]
    public async Task NewInternalConsultant_IsAnOrdinaryConsultantWithAnInternalAllocation()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LT");
        var person = InventoryPerson.NewInternal();

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([InventoryRow.Laptop(type.Name, person)]));
        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal(1, preview.Summary.InternalConsultants);
        Assert.Equal(WorkforceType.Internal, preview.Rows[0].WorkforceType);
        await ConfirmAsync(client, preview.ImportSessionId);

        var state = await LoadPersonStateAsync(person.Nif);
        Assert.NotNull(state.Consultant);
        var allocation = Assert.Single(state.Allocations);
        Assert.Equal(WorkforceType.Internal, allocation.WorkforceType);
        Assert.Equal(EngagementType.EmploymentContract, allocation.EngagementType);
        Assert.Null(allocation.CompanyId);
        Assert.Single(state.Assignments);
    }

    // ---------------------------------------------------------------- D

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedConsultant_IsCreatedOnceWithOneAllocationAndOneAssignmentPerEquipment(bool external)
    {
        using var client = await LoginAsAsync("Administration");
        var (_, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var laptop = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LT");
        var generic = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "AC");
        var person = external ? InventoryPerson.NewExternal(companyName, "João Exemplo") : InventoryPerson.NewInternal("João Exemplo");

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
        [
            InventoryRow.Laptop(laptop.Name, person),
            InventoryRow.Generic(generic.Name, person),
            InventoryRow.Generic(generic.Name, person),
        ]));

        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal(1, preview.Summary.NewConsultants);
        Assert.Equal(1, preview.Summary.NewAllocations);
        Assert.Equal(3, preview.Summary.EquipmentToAssign);

        var confirm = await ConfirmAsync(client, preview.ImportSessionId);
        Assert.Equal(1, confirm.CreatedConsultants);
        Assert.Equal(3, confirm.CreatedEquipment);
        Assert.Equal(3, confirm.CreatedAssignments);

        var state = await LoadPersonStateAsync(person.Nif);
        Assert.Single(state.Allocations);
        Assert.Equal(3, state.Assignments.Count);
        Assert.All(state.Assignments, a => Assert.Equal(AssignmentStatus.Active, a.Status));
        Assert.Equal(1, await CountConsultantsByNifAsync(person.Nif));
    }

    // ---------------------------------------------------------------- E

    [Fact]
    public async Task StockRow_CreatesAvailableEquipmentWithoutConsultantAllocationOrAssignment()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var serial = InventoryRow.NewSerial();
        var consultantsBefore = await CountAsync(db => db.Consultants);

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([InventoryRow.Generic(type.Name, serial: serial)]));
        Assert.Equal(0, preview.ErrorRows);
        Assert.False(preview.Rows[0].WillBeAssigned);
        Assert.Equal(CurrentInventoryConsultantAction.None, preview.Rows[0].ConsultantAction);
        Assert.Equal(1, preview.Summary.EquipmentToStayAvailable);
        Assert.Equal(0, preview.Summary.NewConsultants);

        var confirm = await ConfirmAsync(client, preview.ImportSessionId);
        Assert.Equal(0, confirm.CreatedAssignments);
        Assert.Equal(0, confirm.CreatedConsultants);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var equipment = await db.Equipment.AsNoTracking().SingleAsync(e => e.NormalizedSerialNumber == serial);
        Assert.Equal(EquipmentStatus.Available, equipment.Status);
        Assert.False(await db.EquipmentAssignments.AnyAsync(a => a.EquipmentId == equipment.Id));
        Assert.Equal(consultantsBefore, await db.Consultants.CountAsync());
    }

    // ---------------------------------------------------------------- F

    [Fact]
    public async Task PartialConsultantData_IsRejected_NotTreatedAsStock()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var row = InventoryRow.Generic(type.Name) with { Person = new InventoryPerson("João Exemplo", "", "", "", "", Regime: null, EngagementType: null, Role: null) };

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([row]));

        var result = Assert.Single(preview.Rows);
        Assert.Equal(ImportRowStatus.Error, result.Status);
        Assert.True(result.WillBeAssigned);
        Assert.Contains(result.Issues, i => i.Code == "InvalidNif");
        Assert.Contains(result.Issues, i => i.Code == "MissingWorkforceType");
        Assert.Contains(result.Issues, i => i.Code == "MissingEmail");
        Assert.All(result.Issues, i => Assert.DoesNotContain("Missing", i.Message, StringComparison.Ordinal));

        var confirm = await CurrentInventoryTestData.ConfirmRawAsync(client, preview.ImportSessionId);
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
    }

    [Fact]
    public async Task OnlyAnAssignmentDate_StillMakesTheRowAnAssignedRow()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
            [InventoryRow.Generic(type.Name) with { AssignmentDate = new DateOnly(2025, 5, 1) }]));

        Assert.Equal(ImportRowStatus.Error, preview.Rows[0].Status);
        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "MissingFullName");
    }

    // ---------------------------------------------------------------- G / I

    [Fact]
    public async Task ExistingConsultantWithCompatibleActiveAllocation_IsReused_NoDuplicates()
    {
        using var client = await LoginAsAsync("Administration");
        var (companyId, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewExternal(companyName);
        var consultantId = await CurrentInventoryTestData.CreateConsultantAsync(client, person);
        var allocationId = await CurrentInventoryTestData.CreateAllocationAsync(client, consultantId, person, companyId);

        // Same person, written with different case/spacing: still the same.
        var written = person with { FullName = "  " + person.FullName.ToUpperInvariant() + " ", Email = person.Email.ToUpperInvariant() };
        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
            [InventoryRow.Generic(type.Name, written), InventoryRow.Generic(type.Name, written)]));

        Assert.Equal(0, preview.ErrorRows);
        Assert.All(preview.Rows, r => Assert.Equal(CurrentInventoryConsultantAction.Reuse, r.ConsultantAction));
        Assert.All(preview.Rows, r => Assert.Equal(CurrentInventoryAllocationAction.Reuse, r.AllocationAction));
        Assert.Equal(1, preview.Summary.ReusedConsultants);
        Assert.Equal(1, preview.Summary.ReusedAllocations);
        Assert.Equal(0, preview.Summary.NewConsultants);

        var confirm = await ConfirmAsync(client, preview.ImportSessionId);
        Assert.Equal(0, confirm.CreatedConsultants);
        Assert.Equal(0, confirm.CreatedAllocations);
        Assert.Equal(2, confirm.CreatedAssignments);

        var state = await LoadPersonStateAsync(person.Nif);
        Assert.Equal(consultantId, state.Consultant!.Id);
        Assert.Equal(person.FullName, state.Consultant.FullName);
        Assert.Equal(allocationId, Assert.Single(state.Allocations).Id);
        Assert.Equal(1, await CountConsultantsByNifAsync(person.Nif));
    }

    [Fact]
    public async Task ExistingConsultantWithDifferentPhone_IsAWarning_AndIsNotUpdated()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewInternal();
        var consultantId = await CurrentInventoryTestData.CreateConsultantAsync(client, person);
        await CurrentInventoryTestData.CreateAllocationAsync(client, consultantId, person, null);

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
            [InventoryRow.Generic(type.Name, person with { Phone = "+351911111111" })]));

        Assert.Equal(1, preview.WarningRows);
        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "ExistingConsultantContactDiffers" && i.Severity == ImportIssueSeverity.Warning);
        await ConfirmAsync(client, preview.ImportSessionId);

        var state = await LoadPersonStateAsync(person.Nif);
        Assert.Equal(person.Phone, state.Consultant!.Phone);
    }

    [Fact]
    public async Task ExistingConsultantWithDifferentName_IsAnError()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewInternal();
        await CurrentInventoryTestData.CreateConsultantAsync(client, person);

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
            [InventoryRow.Generic(type.Name, person with { FullName = "Outra Pessoa" })]));

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "ExistingConsultantMismatch");
    }

    // ---------------------------------------------------------------- H

    [Theory]
    [InlineData("Interno", "Externo")]
    [InlineData("Externo", "Interno")]
    public async Task ExistingRegimeConflict_IsAPreviewError_AndNothingChanges(string databaseRegime, string workbookRegime)
    {
        using var client = await LoginAsAsync("Administration");
        var (companyId, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var stored = databaseRegime == "Externo" ? InventoryPerson.NewExternal(companyName) : InventoryPerson.NewInternal();
        var consultantId = await CurrentInventoryTestData.CreateConsultantAsync(client, stored);
        await CurrentInventoryTestData.CreateAllocationAsync(client, consultantId, stored, databaseRegime == "Externo" ? companyId : null);

        var written = workbookRegime == "Externo"
            ? stored with { Regime = "Externo", Company = companyName }
            : stored with { Regime = "Interno", Company = null };
        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([InventoryRow.Generic(type.Name, written)]));

        var issue = Assert.Single(preview.Rows[0].Issues, i => i.Code == "ExistingRegimeConflict");
        Assert.Contains(databaseRegime, issue.Message, StringComparison.Ordinal);
        Assert.Equal(1, preview.ErrorRows);

        var state = await LoadPersonStateAsync(stored.Nif);
        Assert.Equal(databaseRegime == "Externo" ? WorkforceType.External : WorkforceType.Internal, Assert.Single(state.Allocations).WorkforceType);
        Assert.Empty(state.Assignments);
    }

    // ---------------------------------------------------------------- J

    [Fact]
    public async Task ExistingConflictingActiveAllocation_IsAPreviewError()
    {
        using var client = await LoginAsAsync("Administration");
        var (companyId, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var (_, otherCompany) = await Phase2TestData.CreateCompanyAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewExternal(companyName);
        var consultantId = await CurrentInventoryTestData.CreateConsultantAsync(client, person);
        await CurrentInventoryTestData.CreateAllocationAsync(client, consultantId, person, companyId);

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
            [InventoryRow.Generic(type.Name, person with { Company = otherCompany, Role = "Outra função" })]));

        var issue = Assert.Single(preview.Rows[0].Issues, i => i.Code == "ExistingAllocationConflict");
        Assert.Contains("Empresa / Cliente", issue.Message, StringComparison.Ordinal);
        Assert.Contains("Função", issue.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- K

    [Fact]
    public async Task ExistingConsultantWithoutActiveAllocation_GetsANewActiveAllocation()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewInternal();
        var consultantId = await CurrentInventoryTestData.CreateConsultantAsync(client, person);

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([InventoryRow.Generic(type.Name, person)]));
        Assert.Equal(CurrentInventoryConsultantAction.Reuse, preview.Rows[0].ConsultantAction);
        Assert.Equal(CurrentInventoryAllocationAction.Create, preview.Rows[0].AllocationAction);

        var confirm = await ConfirmAsync(client, preview.ImportSessionId);
        Assert.Equal(1, confirm.CreatedAllocations);

        var state = await LoadPersonStateAsync(person.Nif);
        Assert.Equal(consultantId, state.Consultant!.Id);
        var allocation = Assert.Single(state.Allocations);
        Assert.Equal(AllocationStatus.Active, allocation.Status);
        Assert.Equal(person.Start, allocation.StartDate);
    }

    [Fact]
    public async Task ArchivedExistingConsultant_IsAnError()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewInternal();
        var consultantId = await CurrentInventoryTestData.CreateConsultantAsync(client, person);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Consultants.Where(c => c.Id == consultantId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
        }

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([InventoryRow.Generic(type.Name, person)]));

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "ConsultantArchived");
    }

    // ---------------------------------------------------------------- L / M

    [Fact]
    public async Task ExistingSerialNumber_IsRejected_AndTheExistingEquipmentIsUntouched()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var serial = InventoryRow.NewSerial();
        var existing = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id, serial);

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
            [InventoryRow.Generic(type.Name, InventoryPerson.NewInternal(), serial: serial.ToLowerInvariant())]));

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "DuplicateSerialExisting");
        var after = await GetEquipmentAsync(client, existing.Id);
        Assert.Equal(EquipmentStatus.Available, after.Status);
        Assert.Equal(existing.Model, after.Model);
    }

    [Fact]
    public async Task DuplicateSerialOrImeiInsideTheWorkbook_IsRejectedOnEveryRow()
    {
        using var client = await LoginAsAsync("Administration");
        var generic = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var phone = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var serial = InventoryRow.NewSerial();
        var imei = InventoryRow.NewImei();

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
        [
            InventoryRow.Generic(generic.Name, serial: serial),
            InventoryRow.Generic(generic.Name, serial: " " + serial.ToLowerInvariant()),
            InventoryRow.Phone(phone.Name, imei: imei),
            InventoryRow.Phone(phone.Name, imei: $"{imei[..5]}-{imei[5..]}"),
        ]));

        Assert.Equal(4, preview.ErrorRows);
        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "DuplicateSerialInFile");
        Assert.Contains(preview.Rows[1].Issues, i => i.Code == "DuplicateSerialInFile");
        Assert.Contains(preview.Rows[2].Issues, i => i.Code == "DuplicateImeiInFile");
        Assert.Contains(preview.Rows[3].Issues, i => i.Code == "DuplicateImeiInFile");
    }

    // ---------------------------------------------------------------- N

    [Fact]
    public async Task SamePersonWithConflictingRegimeOrCompany_IsRejectedOnEveryRow()
    {
        using var client = await LoginAsAsync("Administration");
        var (_, companyA) = await Phase2TestData.CreateCompanyAsync(client);
        var (_, companyB) = await Phase2TestData.CreateCompanyAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var external = InventoryPerson.NewExternal(companyA);
        var internalThenExternal = InventoryPerson.NewInternal();

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
        [
            InventoryRow.Generic(type.Name, external),
            InventoryRow.Generic(type.Name, external with { Company = companyB }),
            InventoryRow.Generic(type.Name, internalThenExternal),
            InventoryRow.Generic(type.Name, internalThenExternal with { Regime = "Externo", EngagementType = "Prestador de serviço", Company = companyA }),
            InventoryRow.Generic(type.Name, internalThenExternal with { Start = new DateOnly(2024, 1, 1) }),
        ]));

        Assert.Equal(5, preview.ErrorRows);
        Assert.Contains("Empresa / Cliente", Assert.Single(preview.Rows[0].Issues, i => i.Code == "InconsistentConsultantInFile").Message, StringComparison.Ordinal);
        Assert.Contains(preview.Rows[1].Issues, i => i.Code == "InconsistentConsultantInFile");
        var regimeIssue = Assert.Single(preview.Rows[2].Issues, i => i.Code == "InconsistentConsultantInFile");
        Assert.Contains("Regime", regimeIssue.Message, StringComparison.Ordinal);
        Assert.Contains("Data de início", regimeIssue.Message, StringComparison.Ordinal);
        Assert.Contains("linhas 4, 5, 6", regimeIssue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InternalWithCompany_AndExternalWithoutCompany_AreRejected()
    {
        using var client = await LoginAsAsync("Administration");
        var (_, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
        [
            InventoryRow.Generic(type.Name, InventoryPerson.NewInternal() with { Company = companyName }),
            InventoryRow.Generic(type.Name, InventoryPerson.NewExternal(companyName) with { Company = null }),
            InventoryRow.Generic(type.Name, InventoryPerson.NewExternal("Empresa Que Não Existe")),
        ]));

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "CompanyNotAllowedForInternal");
        Assert.Contains(preview.Rows[1].Issues, i => i.Code == "MissingCompany");
        Assert.Contains(preview.Rows[2].Issues, i => i.Code == "UnknownCompany");
    }

    // ---------------------------------------------------------------- O / P

    [Fact]
    public async Task MobilePhone_WithImei_IsImportedWithItsSpecification_AndWithoutPhotos()
    {
        using var client = await LoginAsAsync("Administration");
        var phoneType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var laptopType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LT");
        var person = InventoryPerson.NewInternal();
        var imei = InventoryRow.NewImei();

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
            [InventoryRow.Phone(phoneType.Name, person, imei), InventoryRow.Laptop(laptopType.Name)]));
        Assert.Equal(0, preview.ErrorRows);
        Assert.Equal(EquipmentTypeKind.MobilePhone, preview.Rows[0].EquipmentTypeKind);
        await ConfirmAsync(client, preview.ImportSessionId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var spec = await db.MobilePhoneSpecifications.AsNoTracking().SingleAsync(s => s.NormalizedImei == imei);
        Assert.Equal("+351910000000", spec.MobileNumber);
        Assert.Equal("8935100000000000001", spec.SimSerialNumber);

        // Migration-origin exception: pre-existing Laptop/MobilePhone without an official photo set.
        var phone = await GetEquipmentAsync(client, spec.EquipmentId);
        Assert.Null(phone.CurrentPhotoSet);
        Assert.Equal(EquipmentStatus.Assigned, phone.Status);
        var laptopSerial = preview.Rows[1].SerialNumber!;
        var laptopId = await db.Equipment.Where(e => e.NormalizedSerialNumber == laptopSerial).Select(e => e.Id).SingleAsync();
        Assert.Null((await GetEquipmentAsync(client, laptopId)).CurrentPhotoSet);
        Assert.False(await db.EquipmentPhotoSets.AnyAsync(s => s.EquipmentId == laptopId || s.EquipmentId == spec.EquipmentId));

        // Normal operational creation still requires the photos.
        var csrf = await client.GetAntiforgeryTokenAsync();
        var normal = await Phase3TestData.TryCreateLaptopEquipmentRawAsync(client, laptopType.Id, photoCount: 0, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, normal.StatusCode);
    }

    [Fact]
    public async Task MobilePhone_WithoutImei_OrWithAnImeiAlreadyInUse_IsRejected()
    {
        using var client = await LoginAsAsync("Administration");
        var phoneType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var usedImei = InventoryRow.NewImei();
        await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, phoneType.Id, usedImei);

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
        [
            InventoryRow.Phone(phoneType.Name) with { Imei = null },
            InventoryRow.Phone(phoneType.Name, imei: usedImei),
            InventoryRow.Phone(phoneType.Name) with { Manufacturer = null },
            InventoryRow.Phone(phoneType.Name) with { Imei = " - - " },
            InventoryRow.Phone(phoneType.Name) with { Imei = "35.693803/564380-9" },
            InventoryRow.Phone(phoneType.Name, imei: SpacedImei(InventoryRow.NewImei())),
        ]));

        Assert.Contains(preview.Rows[3].Issues, i => i.Code == "MissingImei");
        var malformed = Assert.Single(preview.Rows[4].Issues, i => i.Code == "InvalidImei");
        Assert.Equal("O campo \"IMEI\" deve conter exatamente 15 dígitos.", malformed.Message);
        Assert.Equal(ImportRowStatus.Valid, preview.Rows[5].Status);

        static string SpacedImei(string imei) => $"{imei[..3]} {imei[3..6]} {imei[6..9]} {imei[9..12]} {imei[12..]}";

        var missing = Assert.Single(preview.Rows[0].Issues, i => i.Code == "MissingImei");
        Assert.Equal("O campo \"IMEI\" é obrigatório para telemóveis.", missing.Message);
        Assert.Contains(preview.Rows[1].Issues, i => i.Code == "DuplicateImeiExisting");
        Assert.Contains(preview.Rows[2].Issues, i => i.Code == "MissingManufacturer");
    }

    [Fact]
    public async Task KindSpecificColumns_AreOnlyAcceptedForTheirKind()
    {
        using var client = await LoginAsAsync("Administration");
        var generic = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var laptop = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LT");

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
        [
            InventoryRow.Generic(generic.Name) with { Processor = "i5" },
            InventoryRow.Laptop(laptop.Name) with { Imei = InventoryRow.NewImei() },
            InventoryRow.Laptop(laptop.Name) with { Storage = null },
        ]));

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "LaptopFieldsNotAllowed");
        Assert.Contains(preview.Rows[1].Issues, i => i.Code == "MobileFieldsNotAllowed");
        Assert.Contains(preview.Rows[2].Issues, i => i.Code == "MissingLaptopStorage");
    }

    // ---------------------------------------------------------------- Q

    [Fact]
    public async Task AssetTags_AreAllocatedOnlyAtConfirmation_FromTheSharedSequence()
    {
        using var client = await LoginAsAsync("Administration");
        var laptop = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LT");
        var generic = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var before = await Phase3TestData.CreateGenericEquipmentAsync(client, generic.Id);
        var beforeNumber = TagNumber(before.AssetTag);

        var rows = new[] { InventoryRow.Laptop(laptop.Name), InventoryRow.Laptop(laptop.Name), InventoryRow.Generic(generic.Name) };
        var sequenceBeforePreview = await AssetTagSequenceLastValueAsync();
        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(rows));
        Assert.Equal(sequenceBeforePreview, await AssetTagSequenceLastValueAsync());
        Assert.False(await AnySerialExistsAsync(rows[0].SerialNumber!));

        await ConfirmAsync(client, preview.ImportSessionId);

        var tags = new List<string>();
        foreach (var row in rows)
        {
            tags.Add(await AssetTagOfSerialAsync(row.SerialNumber!));
        }

        Assert.StartsWith("LT-", tags[0], StringComparison.Ordinal);
        Assert.StartsWith("LT-", tags[1], StringComparison.Ordinal);
        Assert.StartsWith("MN-", tags[2], StringComparison.Ordinal);
        Assert.Equal(3, tags.Distinct().Count());
        Assert.All(tags, t => Assert.True(TagNumber(t) > beforeNumber));
        Assert.True(TagNumber(tags[0]) < TagNumber(tags[1]) && TagNumber(tags[1]) < TagNumber(tags[2]));

        // Retire an imported (Available) one: its tag is never reused by later creation.
        var stockId = await EquipmentIdOfSerialAsync(rows[2].SerialNumber!);
        var csrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync<object?>($"/api/equipment/{stockId}/retire", null, csrf)).EnsureSuccessStatusCode();
        var after = await Phase3TestData.CreateGenericEquipmentAsync(client, generic.Id);
        Assert.True(TagNumber(after.AssetTag) > TagNumber(tags[2]));
        Assert.DoesNotContain(after.AssetTag, tags);
    }

    // ---------------------------------------------------------------- R

    [Fact]
    public async Task FailureDuringConfirmation_LeavesNoPartialImport()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var first = InventoryPerson.NewInternal();
        var second = InventoryPerson.NewInternal();
        var rows = new[]
        {
            InventoryRow.Generic(type.Name, first),
            InventoryRow.Generic(type.Name),
            InventoryRow.Generic(type.Name, second),
        };

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(rows));
        Assert.Equal(0, preview.ErrorRows);

        // Inject a database failure on the LAST assignment insert, after
        // consultants, allocations, every equipment and the first assignment
        // were already written inside the transaction.
        var trigger = $"ci_fail_{Guid.NewGuid():N}"[..20];
        await ExecuteSqlAsync($"""
            CREATE FUNCTION {trigger}() RETURNS trigger AS $$
            BEGIN
                IF NEW."ConsultantId" IN (SELECT "Id" FROM "Consultants" WHERE "Nif" = '{second.Nif}') THEN
                    RAISE EXCEPTION 'injected test failure' USING ERRCODE = 'unique_violation';
                END IF;
                RETURN NEW;
            END $$ LANGUAGE plpgsql;
            CREATE TRIGGER {trigger} BEFORE INSERT ON "EquipmentAssignments" FOR EACH ROW EXECUTE FUNCTION {trigger}();
            """);
        try
        {
            var response = await CurrentInventoryTestData.ConfirmRawAsync(client, preview.ImportSessionId);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        finally
        {
            await ExecuteSqlAsync($"""DROP TRIGGER {trigger} ON "EquipmentAssignments"; DROP FUNCTION {trigger}();""");
        }

        Assert.Equal(0, await CountConsultantsByNifAsync(first.Nif));
        Assert.Equal(0, await CountConsultantsByNifAsync(second.Nif));
        foreach (var row in rows)
        {
            Assert.False(await AnySerialExistsAsync(row.SerialNumber!));
        }

        var session = await GetSessionAsync(client, preview.ImportSessionId);
        Assert.Equal(ImportSessionStatus.Pending, session.Status);

        // Nothing was left half-done: the same session confirms cleanly afterwards.
        var retry = await ConfirmAsync(client, preview.ImportSessionId);
        Assert.Equal(2, retry.CreatedConsultants);
        Assert.Equal(3, retry.CreatedEquipment);
    }

    // ---------------------------------------------------------------- S

    [Fact]
    public async Task Stale_ConsultantCreatedAfterPreview_IsCaughtAtConfirmation()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewInternal();
        var row = InventoryRow.Generic(type.Name, person);
        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([row]));

        await CurrentInventoryTestData.CreateConsultantAsync(client, person);

        var conflict = await ConfirmExpectingStaleAsync(client, preview.ImportSessionId);
        Assert.Contains(conflict.Rows[0].Issues, i => i.Code == "ConsultantChanged");
        Assert.False(await AnySerialExistsAsync(row.SerialNumber!));
    }

    [Fact]
    public async Task Stale_RegimeOrAllocationChangedAfterPreview_IsCaughtAtConfirmation()
    {
        using var client = await LoginAsAsync("Administration");
        var (companyId, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewInternal();
        var consultantId = await CurrentInventoryTestData.CreateConsultantAsync(client, person);
        var allocationId = await CurrentInventoryTestData.CreateAllocationAsync(client, consultantId, person, null);
        var row = InventoryRow.Generic(type.Name, person);
        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([row]));
        Assert.Equal(CurrentInventoryAllocationAction.Reuse, preview.Rows[0].AllocationAction);

        // The Internal allocation is ended and an External one registered instead.
        (await Phase4TestData.EndAllocationAsync(client, consultantId, allocationId, new DateOnly(2026, 6, 30))).EnsureSuccessStatusCode();
        await CurrentInventoryTestData.CreateAllocationAsync(
            client, consultantId, person with { Regime = "Externo", EngagementType = "Prestador de serviço", Company = companyName, Start = new DateOnly(2026, 7, 1) }, companyId);

        var conflict = await ConfirmExpectingStaleAsync(client, preview.ImportSessionId);
        Assert.Contains(conflict.Rows[0].Issues, i => i.Code == "ExistingRegimeConflict");
        Assert.False(await AnySerialExistsAsync(row.SerialNumber!));
    }

    [Fact]
    public async Task Stale_AllocationEndedAfterPreview_IsCaughtAtConfirmation()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewInternal();
        var consultantId = await CurrentInventoryTestData.CreateConsultantAsync(client, person);
        var allocationId = await CurrentInventoryTestData.CreateAllocationAsync(client, consultantId, person, null);
        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([InventoryRow.Generic(type.Name, person)]));

        (await Phase4TestData.EndAllocationAsync(client, consultantId, allocationId, new DateOnly(2026, 6, 30))).EnsureSuccessStatusCode();

        var conflict = await ConfirmExpectingStaleAsync(client, preview.ImportSessionId);
        Assert.Contains(conflict.Rows[0].Issues, i => i.Code == "AllocationChanged");
        var state = await LoadPersonStateAsync(person.Nif);
        Assert.All(state.Allocations, a => Assert.Equal(AllocationStatus.Ended, a.Status));
    }

    [Fact]
    public async Task Stale_DuplicateEquipmentOrDeactivatedTypeAfterPreview_IsCaughtAtConfirmation()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var otherType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "AC");
        var rows = new[] { InventoryRow.Generic(type.Name), InventoryRow.Generic(otherType.Name) };
        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(rows));

        await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id, rows[0].SerialNumber);
        var csrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync<object?>($"/api/equipment-types/{otherType.Id}/deactivate", null, csrf)).EnsureSuccessStatusCode();

        var conflict = await ConfirmExpectingStaleAsync(client, preview.ImportSessionId);
        Assert.Contains(conflict.Rows[0].Issues, i => i.Code == "DuplicateSerialExisting");
        Assert.Contains(conflict.Rows[1].Issues, i => i.Code == "InactiveEquipmentType");
        Assert.False(await AnySerialExistsAsync(rows[1].SerialNumber!));
    }

    // ---------------------------------------------------------------- T

    [Fact]
    public async Task Confirmation_CreatesNoHandoverTermDispatchReturnOrEmail()
    {
        using var client = await LoginAsAsync("Administration");
        var (_, companyName) = await Phase2TestData.CreateCompanyAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewExternal(companyName);
        var recorder = factory.Services.GetRequiredService<RecordingEmailSender>();
        var emailsBefore = recorder.SentMessages.Count;

        var (handovers, terms, dispatches, returns) = await CountWorkflowRowsAsync();
        await CurrentInventoryTestData.PreviewAndConfirmAsync(client, [InventoryRow.Generic(type.Name, person), InventoryRow.Generic(type.Name, person)]);

        Assert.Equal((handovers, terms, dispatches, returns), await CountWorkflowRowsAsync());
        Assert.DoesNotContain(recorder.SentMessages.Skip(emailsBefore), m => m.ToAddress == person.Email);
        Assert.Equal(emailsBefore, recorder.SentMessages.Count);
    }

    [Fact]
    public async Task ImportedCustody_WorksWithTheNormalReturnWorkflow()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewInternal();
        var row = InventoryRow.Generic(type.Name, person);
        await CurrentInventoryTestData.PreviewAndConfirmAsync(client, [row]);
        var equipmentId = await EquipmentIdOfSerialAsync(row.SerialNumber!);

        var assignmentId = await Phase5TestData.GetActiveAssignmentIdAsync(client, equipmentId);
        var equipmentReturn = await Phase5TestData.InitiateReturnSuccessfullyAsync(client, assignmentId);
        var (receiverEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: $"Rua Receiver {Guid.NewGuid():N}, Lisboa");
        var receiverId = await ApiTestUsers.GetIdAsync(factory, receiverEmail);
        (await Phase5TestData.ReceiveReturnAsync(client, equipmentReturn.Id, DateOnly.FromDateTime(DateTime.UtcNow), receiverId)).EnsureSuccessStatusCode();
        (await Phase5TestData.RecordReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();
        (await Phase5TestData.FinalizeReturnInspectionAsync(client, equipmentReturn.Id)).EnsureSuccessStatusCode();

        var equipment = await GetEquipmentAsync(client, equipmentId);
        Assert.Null(equipment.CurrentCustody);
        Assert.Equal(EquipmentStatus.Available, equipment.Status);

        var history = await client.GetFromJsonAsync<List<EquipmentHistoryEventResponse>>(
            $"/api/equipment/{equipmentId}/history", HttpClientTestExtensions.JsonOptions);
        var assigned = Assert.Single(history!, e => e.EventType == EquipmentHistoryEventType.Assigned);
        Assert.Equal(EquipmentAssignmentOrigin.CurrentInventoryImport, assigned.AssignmentOrigin);
        Assert.Equal(person.FullName, assigned.ConsultantName);
        Assert.Null(assigned.HandoverId);
    }

    // ---------------------------------------------------------------- U

    [Fact]
    public async Task AssignmentDate_IsPreservedWhenGiven_AndIsTheConfirmationTimeWhenBlank()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var person = InventoryPerson.NewInternal();
        var dated = InventoryRow.Generic(type.Name, person) with { AssignmentDate = new DateOnly(2024, 11, 18) };
        var undated = InventoryRow.Generic(type.Name, person);

        var before = DateTime.UtcNow.AddSeconds(-1);
        await CurrentInventoryTestData.PreviewAndConfirmAsync(client, [dated, undated]);
        var after = DateTime.UtcNow.AddSeconds(1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var datedStart = await StartOfSerialAsync(db, dated.SerialNumber!);
        var undatedStart = await StartOfSerialAsync(db, undated.SerialNumber!);

        Assert.Equal(new DateTime(2024, 11, 18, 0, 0, 0, DateTimeKind.Utc), datedStart);
        Assert.InRange(undatedStart, before, after);
    }

    [Fact]
    public async Task AssignmentDateInTheFuture_IsRejected()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");
        var row = InventoryRow.Generic(type.Name, InventoryPerson.NewInternal()) with { AssignmentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2) };

        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([row]));

        Assert.Contains(preview.Rows[0].Issues, i => i.Code == "AssignmentDateInFuture");
    }

    // ---------------------------------------------------------------- V

    [Fact]
    public async Task Hr_CanRunTheWholeWorkflow()
    {
        using var client = await LoginAsAsync("HR");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Generic", assetTagPrefix: "MN");

        (await client.GetAsync($"{CurrentInventoryTestData.BaseUrl}/template")).EnsureSuccessStatusCode();
        var confirm = await CurrentInventoryTestData.PreviewAndConfirmAsync(client, [InventoryRow.Generic(type.Name, InventoryPerson.NewInternal())]);
        Assert.Equal(1, confirm.CreatedAssignments);
    }

    [Fact]
    public async Task AnonymousOrRolelessUsers_AreRejected()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{CurrentInventoryTestData.BaseUrl}/template")).StatusCode);

        var email = $"semfuncao.{Guid.NewGuid():N}@example.invalid";
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<EquipmentManagement.Infrastructure.Identity.ApplicationUser>>();
            var created = await userManager.CreateAsync(new EquipmentManagement.Infrastructure.Identity.ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, FirstName = "Sem", LastName = "Função",
                IsActive = true, MustChangePassword = false,
            }, ApiTestUsers.DefaultPassword);
            Assert.True(created.Succeeded);
        }

        using var roleless = factory.CreateClient();
        await roleless.LoginAsync(email, ApiTestUsers.DefaultPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await roleless.GetAsync($"{CurrentInventoryTestData.BaseUrl}/template")).StatusCode);
        var preview = await CurrentInventoryTestData.PreviewRawAsync(roleless, CurrentInventoryTestData.BuildWorkbook([]));
        Assert.Equal(HttpStatusCode.Forbidden, preview.StatusCode);
    }

    // ---------------------------------------------------------------- session / audit

    [Fact]
    public async Task Confirmation_IsAuditedWithCountsAndIds_WithoutWorkbookContent()
    {
        using var client = await LoginAsAsync("Administration");
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var person = InventoryPerson.NewInternal("Nome Muito Distinto Auditoria");
        var imei = InventoryRow.NewImei();
        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook(
            [InventoryRow.Phone(type.Name, person, imei) with { Notes = "Nota livre que não pode ir para a auditoria" }]));
        await ConfirmAsync(client, preview.ImportSessionId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entry = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == "CurrentInventoryImport.Completed" && a.EntityId == preview.ImportSessionId.ToString());
        using var json = System.Text.Json.JsonDocument.Parse(entry.NewValuesJson!);
        var values = json.RootElement;
        Assert.Equal(1, values.GetProperty("createdAssignmentCount").GetInt32());
        Assert.Equal(1, values.GetProperty("createdConsultantCount").GetInt32());
        Assert.Equal(1, values.GetProperty("internalConsultantCount").GetInt32());
        Assert.Equal("CurrentInventoryImport", values.GetProperty("importOrigin").GetString());
        Assert.Equal(1, values.GetProperty("createdEquipmentIds").GetArrayLength());
        Assert.DoesNotContain(person.Nif, entry.NewValuesJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(person.Email, entry.NewValuesJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(person.FullName, entry.NewValuesJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(imei, entry.NewValuesJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("Nota livre", entry.NewValuesJson!, StringComparison.Ordinal);

        var session = await db.ImportSessions.AsNoTracking().SingleAsync(s => s.Id == preview.ImportSessionId);
        Assert.Equal(ImportSessionStatus.Completed, session.Status);
        Assert.Null(session.RowsJson);
    }

    [Fact]
    public async Task PreviewWithErrors_CannotBeConfirmed_AndCancelClearsTheSnapshot()
    {
        using var client = await LoginAsAsync("Administration");
        var preview = await CurrentInventoryTestData.PreviewAsync(client, CurrentInventoryTestData.BuildWorkbook([new InventoryRow("Tipo Inexistente")]));
        Assert.Equal(1, preview.ErrorRows);
        Assert.Equal(HttpStatusCode.Conflict, (await CurrentInventoryTestData.ConfirmRawAsync(client, preview.ImportSessionId)).StatusCode);

        var csrf = await client.GetAntiforgeryTokenAsync();
        (await client.PostWithCsrfAsync<object?>($"{CurrentInventoryTestData.BaseUrl}/{preview.ImportSessionId}/cancel", null, csrf)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await db.ImportSessions.AsNoTracking().SingleAsync(s => s.Id == preview.ImportSessionId);
        Assert.Equal(ImportSessionStatus.Cancelled, session.Status);
        Assert.Equal(ImportType.CurrentInventory, session.ImportType);
        Assert.Null(session.RowsJson);
    }

    [Fact]
    public async Task MissingColumn_IsRejectedBeforeAnyRowIsRead()
    {
        using var client = await LoginAsAsync("Administration");
        var bytes = CurrentInventoryTestData.BuildWorkbook([], sheet => sheet.Column(13).Delete());

        var response = await CurrentInventoryTestData.PreviewRawAsync(client, bytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("NIF", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record PersonState(
        EquipmentManagement.Domain.Consultants.Consultant? Consultant,
        List<ConsultantAllocation> Allocations,
        List<EquipmentAssignment> Assignments);

    private async Task<PersonState> LoadPersonStateAsync(string nif)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var consultant = await db.Consultants.AsNoTracking().SingleOrDefaultAsync(c => c.Nif == nif);
        if (consultant is null)
        {
            return new PersonState(null, [], []);
        }

        var allocations = await db.ConsultantAllocations.AsNoTracking().Where(a => a.ConsultantId == consultant.Id).ToListAsync();
        var assignments = await db.EquipmentAssignments.AsNoTracking().Where(a => a.ConsultantId == consultant.Id).ToListAsync();
        return new PersonState(consultant, allocations, assignments);
    }

    private async Task<int> CountConsultantsByNifAsync(string nif) => await CountAsync(db => db.Consultants.Where(c => c.Nif == nif));

    private async Task<int> CountAsync<T>(Func<ApplicationDbContext, IQueryable<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()).CountAsync();
    }

    private async Task<(int, int, int, int)> CountWorkflowRowsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.EquipmentHandovers.CountAsync(), await db.ResponsibilityTerms.CountAsync(),
            await db.EquipmentDispatches.CountAsync(), await db.EquipmentReturns.CountAsync());
    }

    private async Task<bool> AnySerialExistsAsync(string serial)
    {
        var normalized = serial.ToUpperInvariant();
        return await CountAsync(db => db.Equipment.Where(e => e.NormalizedSerialNumber == normalized)) > 0;
    }


    private async Task<string> AssetTagOfSerialAsync(string serial)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Equipment.Where(e => e.NormalizedSerialNumber == serial).Select(e => e.AssetTag).SingleAsync();
    }

    private async Task<Guid> EquipmentIdOfSerialAsync(string serial)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Equipment.Where(e => e.NormalizedSerialNumber == serial).Select(e => e.Id).SingleAsync();
    }

    private static async Task<DateTime> StartOfSerialAsync(ApplicationDbContext db, string serial) =>
        await (from e in db.Equipment
               join a in db.EquipmentAssignments on e.Id equals a.EquipmentId
               where e.NormalizedSerialNumber == serial
               select a.StartedAtUtc).SingleAsync();

    private async Task<long> AssetTagSequenceLastValueAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Database.SqlQueryRaw<long>($"SELECT last_value AS \"Value\" FROM {ApplicationDbContext.AssetTagSequenceName}").SingleAsync();
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        TestDatabase.EnsureIsThisRunsDatabase(db.Database.GetConnectionString()!);
#pragma warning disable EF1002 // Test-only DDL built from constants and generated identifiers.
        await db.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }

    private static long TagNumber(string assetTag) => long.Parse(assetTag[(assetTag.IndexOf('-', StringComparison.Ordinal) + 1)..], System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<EquipmentDetailResponse> GetEquipmentAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{id}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<CurrentInventoryImportPreviewResponse> GetSessionAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<CurrentInventoryImportPreviewResponse>($"{CurrentInventoryTestData.BaseUrl}/{id}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<CurrentInventoryImportConfirmResponse> ConfirmAsync(HttpClient client, Guid sessionId)
    {
        var response = await CurrentInventoryTestData.ConfirmRawAsync(client, sessionId);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CurrentInventoryImportConfirmResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<CurrentInventoryImportStaleConflictResponse> ConfirmExpectingStaleAsync(HttpClient client, Guid sessionId)
    {
        var response = await CurrentInventoryTestData.ConfirmRawAsync(client, sessionId);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CurrentInventoryImportStaleConflictResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private async Task<HttpClient> LoginAsAsync(string role)
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, role, address: role == "HR" ? "Rua do RH 1, Lisboa" : null);
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
