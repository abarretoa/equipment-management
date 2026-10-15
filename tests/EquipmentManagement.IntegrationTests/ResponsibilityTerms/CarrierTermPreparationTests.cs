using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using EquipmentManagement.Api.Contracts.Equipments;
using EquipmentManagement.Api.Contracts.Handovers;
using EquipmentManagement.Api.Contracts.ResponsibilityTerms;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.ResponsibilityTerms;

/// <summary>
/// Carrier delivery: once "Registar envio" locks the composition, RH may
/// generate (prepare) the Responsibility Term — its snapshot frozen at that
/// generation — while the equipment is still in transit. Custody, sending and
/// the signed copy all stay strictly after "Confirmar entrega", and that
/// confirmation keeps the prepared term as the current version (no v2).
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class CarrierTermPreparationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Generate_CarrierDraftBeforeDispatch_IsRejected_AndNoTermExists()
    {
        using var client = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(client, dispatch: false);

        var response = await GenerateAsync(client, carrier.HandoverId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await ListTermsAsync(client, carrier.HandoverId));
    }

    [Fact]
    public async Task Generate_InPersonDraft_IsStillRejectedBeforeConfirmation()
    {
        using var client = await LoginAsync("HR");
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var equipment = await Phase3TestData.CreateGenericEquipmentAsync(client, type.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, equipment.Id, photoCount: 0);

        var response = await GenerateAsync(client, handover.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await ListTermsAsync(client, handover.Id));
    }

    [Fact]
    public async Task Generate_AfterDispatch_Succeeds_WithoutAnyCustodyOrDeliveryState()
    {
        using var client = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(client);

        Assert.Equal(EquipmentStatus.InTransit, (await GetEquipmentAsync(client, carrier.LaptopId)).Status);

        var term = await GenerateSuccessfullyAsync(client, carrier.HandoverId);

        Assert.Equal(1, term.Version);
        Assert.Equal(ResponsibilityTermStatus.Generated, term.Status);

        foreach (var equipmentId in carrier.EquipmentIds)
        {
            var equipment = await GetEquipmentAsync(client, equipmentId);
            Assert.Equal(EquipmentStatus.InTransit, equipment.Status);
            Assert.Null(equipment.CurrentCustody);
        }

        var handover = await GetHandoverAsync(client, carrier.HandoverId);
        Assert.Equal(HandoverStatus.Draft, handover.Status);
        Assert.Equal(HandoverOperationalStatus.Draft, handover.OperationalStatus);
        Assert.Null(handover.FinalizedAtUtc);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await dbContext.EquipmentAssignments.AnyAsync(a => a.EquipmentHandoverId == carrier.HandoverId));
    }

    [Fact]
    public async Task Generate_AfterDispatch_FreezesConsultantAllocationAndEquipment_AgainstLaterEdits()
    {
        using var client = await LoginAsync("Administration");
        var carrier = await CreateCarrierDraftAsync(client);
        var consultantBefore = await client.GetFromJsonAsync<Api.Contracts.Consultants.ConsultantDetailResponse>(
            $"/api/consultants/{carrier.ConsultantId}", HttpClientTestExtensions.JsonOptions);

        var term = await GenerateSuccessfullyAsync(client, carrier.HandoverId);

        // The version owns what it was rendered from; the Draft still has no
        // delivery record of its own.
        var (prepared, preparedItems) = await TermSnapshotTestData.GetAsync(factory, term.Id);
        Assert.False(prepared.SnapshotUnavailable);
        Assert.Equal(consultantBefore!.FullName, prepared.ConsultantSnapshot!.FullName);
        Assert.Equal(consultantBefore.Address, prepared.ConsultantSnapshot.Address);
        Assert.Equal(consultantBefore.Email, prepared.ConsultantSnapshot.Email);
        Assert.Equal("Internal", prepared.AllocationSnapshot!.WorkforceType);
        Assert.Equal("Backend Developer", prepared.AllocationSnapshot.RoleOrFunction);
        Assert.Equal(carrier.AssetTags.Order(), preparedItems.Select(i => i.EquipmentSnapshot.AssetTag).Order());
        Assert.Null((await GetHandoverAsync(client, carrier.HandoverId)).ConsultantSnapshot);

        // Live data edited after the term was generated...
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var consultantUpdate = await client.PutWithCsrfAsync($"/api/consultants/{carrier.ConsultantId}", new
        {
            FullName = "Renamed After Generation",
            consultantBefore.Nif,
            consultantBefore.Email,
            Address = "Nova Morada 99, Porto",
            consultantBefore.Phone,
        }, csrfToken);
        Assert.True(consultantUpdate.IsSuccessStatusCode, await consultantUpdate.Content.ReadAsStringAsync());
        var laptopBefore = await GetEquipmentAsync(client, carrier.LaptopId);
        var equipmentUpdate = await client.PutWithCsrfAsync($"/api/equipment/{carrier.LaptopId}", new
        {
            Manufacturer = "Dell",
            Model = "Changed After Generation",
            laptopBefore.SerialNumber,
            Notes = (string?)null,
            LaptopSpecification = new { Processor = "Intel i9", Memory = "64 GB", Storage = "2 TB SSD" },
        }, csrfToken);
        Assert.True(equipmentUpdate.IsSuccessStatusCode, await equipmentUpdate.Content.ReadAsStringAsync());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var allocation = await dbContext.ConsultantAllocations.FirstAsync(a => a.ConsultantId == carrier.ConsultantId);
            allocation.RoleOrFunction = "Changed After Generation";
            await dbContext.SaveChangesAsync();
        }

        // ...and physical receipt confirmed: nothing the term was built from changes.
        await ConfirmDeliveryAsync(client, carrier.HandoverId);

        var finalized = await GetHandoverAsync(client, carrier.HandoverId);
        Assert.Equal(consultantBefore.FullName, finalized.ConsultantSnapshot!.FullName);
        Assert.Equal(consultantBefore.Address, finalized.ConsultantSnapshot.Address);
        Assert.Equal("Backend Developer", finalized.AllocationSnapshot!.RoleOrFunction);
        var laptopItem = finalized.Items.Single(i => i.EquipmentId == carrier.LaptopId);
        Assert.Equal("Latitude 5450", laptopItem.EquipmentSnapshot!.Model);
        Assert.Equal("Intel i7-1365U", laptopItem.EquipmentSnapshot.Processor);

        var current = Assert.Single(await ListTermsAsync(client, carrier.HandoverId));
        Assert.Equal(term.Id, current.Id);
        Assert.Equal(term.Sha256, current.Sha256);

        // The version's own snapshot never moved either.
        var (after, afterItems) = await TermSnapshotTestData.GetAsync(factory, term.Id);
        Assert.Equal(consultantBefore.FullName, after.ConsultantSnapshot!.FullName);
        Assert.Equal("Backend Developer", after.AllocationSnapshot!.RoleOrFunction);
        Assert.Equal("Latitude 5450", afterItems.Single(i => i.EquipmentId == carrier.LaptopId).EquipmentSnapshot.Model);
    }

    [Fact]
    public async Task Generate_AfterDispatch_FreezesTheCurrentPhotoSets_AndALaterPhotoSetNeverReplacesThem()
    {
        using var client = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(client);
        var laptopSetAtGeneration = await GetCurrentPhotoSetIdAsync(carrier.LaptopId);
        var phoneSetAtGeneration = await GetCurrentPhotoSetIdAsync(carrier.PhoneId);

        var term = await GenerateSuccessfullyAsync(client, carrier.HandoverId);

        Assert.Equal(laptopSetAtGeneration, await TermSnapshotTestData.GetPhotoSetIdAsync(factory, term.Id, carrier.LaptopId));
        Assert.Equal(phoneSetAtGeneration, await TermSnapshotTestData.GetPhotoSetIdAsync(factory, term.Id, carrier.PhoneId));
        Assert.Null(await TermSnapshotTestData.GetPhotoSetIdAsync(factory, term.Id, carrier.GenericId));

        // A new Current photo set for the laptop appears before receipt.
        var newLaptopSetId = await ReplaceCurrentPhotoSetAsync(carrier.LaptopId);

        await ConfirmDeliveryAsync(client, carrier.HandoverId);

        // The version keeps its own link, and the delivery record is copied from it.
        Assert.Equal(laptopSetAtGeneration, await TermSnapshotTestData.GetPhotoSetIdAsync(factory, term.Id, carrier.LaptopId));
        Assert.Equal(laptopSetAtGeneration, await GetFrozenPhotoSetIdAsync(carrier.HandoverId, carrier.LaptopId));
        Assert.NotEqual(newLaptopSetId, await GetFrozenPhotoSetIdAsync(carrier.HandoverId, carrier.LaptopId));
        var current = Assert.Single(await ListTermsAsync(client, carrier.HandoverId));
        Assert.Equal(term.Sha256, current.Sha256);
    }

    [Fact]
    public async Task Generate_AfterDispatch_StillRequiresTheCurrentPhotoSet()
    {
        using var client = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(client);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sets = await dbContext.EquipmentPhotoSets.Where(s => s.EquipmentId == carrier.LaptopId).ToListAsync();
            dbContext.EquipmentPhotoSets.RemoveRange(sets);
            await dbContext.SaveChangesAsync();
        }

        var response = await GenerateAsync(client, carrier.HandoverId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("fotografias", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(await ListTermsAsync(client, carrier.HandoverId));
        Assert.Null((await GetHandoverAsync(client, carrier.HandoverId)).ConsultantSnapshot);
    }

    [Fact]
    public async Task Generate_AfterDispatch_StillRequiresEveryDeliveryInspection()
    {
        using var client = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(client, inspectGeneric: false);

        var response = await GenerateAsync(client, carrier.HandoverId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await ListTermsAsync(client, carrier.HandoverId));
    }

    [Fact]
    public async Task PreparedPdf_HasTheBlankReceptionDate_AndNeverStatesTheDeliveryDate()
    {
        using var client = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(client);
        // RH may already have typed a date into the Draft header — it is not
        // a confirmed receipt and must never reach the prepared document.
        (await Phase4TestData.SetDeliveredOnAsync(client, carrier.HandoverId, new DateOnly(2026, 1, 15))).EnsureSuccessStatusCode();

        var term = await GenerateSuccessfullyAsync(client, carrier.HandoverId);
        var text = ExtractShownText(await DownloadAsync(client, term.Id));

        Assert.Equal(1, CountOccurrences(text, "Datadereceçãodosequipamentos:"));
        Assert.Equal(1, CountOccurrences(text, "____/____/______"));
        Assert.Equal(1, CountOccurrences(text, @"Assinaturado\(a\)Consultor\(a\):"));
        Assert.True(
            text.IndexOf(@"Assinaturado\(a\)Consultor\(a\):", StringComparison.Ordinal) <
            text.IndexOf("Datadereceçãodosequipamentos:", StringComparison.Ordinal));
        Assert.DoesNotContain("15/01/2026", text, StringComparison.Ordinal);
        foreach (var label in new[] { "Datadeentrega", "DatadeEntrega", "DatadeEmpréstimo", "Entreguepor", "EquipamentosEntregues" })
        {
            Assert.DoesNotContain(label, text, StringComparison.Ordinal);
        }

        AssertNoObsoleteSignatureFields(text);

        // Every date the document shows is the generation timestamp or a
        // photo-set capture date — never a delivery date.
        var dates = Regex.Matches(text, @"\d{2}/\d{2}/\d{4}").Select(m => m.Value).ToList();
        Assert.NotEmpty(dates);
        var explained = Regex.Count(text, @"(Documentogeradoem|Registadasem:)\d{2}/\d{2}/\d{4}");
        Assert.Equal(dates.Count, explained);
    }

    [Fact]
    public async Task BeforeConfirmation_SendAndSignedUploadAreRejected_AndNothingIsRecorded()
    {
        using var client = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(client);
        var term = await GenerateSuccessfullyAsync(client, carrier.HandoverId);

        var send = await Phase7TestData.SendAsync(client, term.Id);
        var upload = await Phase7TestData.UploadSignedDocumentAsync(client, term.Id);

        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
        Assert.Contains("confirmada a entrega", await send.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode);
        Assert.Contains("confirmada a entrega", await upload.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await dbContext.ResponsibilityTermDeliveries.AnyAsync(d => d.ResponsibilityTermId == term.Id));
        Assert.False(await dbContext.ResponsibilityTermSignedArtifacts.AnyAsync(a => a.ResponsibilityTermId == term.Id));
        var recipient = (await TermSnapshotTestData.GetAsync(factory, term.Id)).Term.ConsultantSnapshot!.Email;
        Assert.DoesNotContain(GetEmailSender().SentMessages, m => m.ToAddress == recipient && m.Attachment is not null);

        var handover = await GetHandoverAsync(client, carrier.HandoverId);
        Assert.Equal(HandoverOperationalStatus.Draft, handover.OperationalStatus);
        Assert.NotEqual(HandoverOperationalStatus.Completed, handover.OperationalStatus);
    }

    [Fact]
    public async Task Confirmation_RecordsReceipt_CreatesCustody_AndKeepsThePreparedTermCurrent_WithoutAV2()
    {
        using var client = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(client);
        var term = await GenerateSuccessfullyAsync(client, carrier.HandoverId);
        var bytesBefore = await DownloadAsync(client, term.Id);
        var deliveredOn = DateOnly.FromDateTime(DateTime.UtcNow);

        (await Phase4TestData.SetDeliveredOnAsync(client, carrier.HandoverId, deliveredOn)).EnsureSuccessStatusCode();
        await ConfirmDeliveryAsync(client, carrier.HandoverId);

        var handover = await GetHandoverAsync(client, carrier.HandoverId);
        Assert.Equal(HandoverStatus.Finalized, handover.Status);
        Assert.Equal(deliveredOn, handover.DeliveredOn);
        Assert.Equal(HandoverOperationalStatus.AwaitingSignedTerm, handover.OperationalStatus);

        foreach (var equipmentId in carrier.EquipmentIds)
        {
            var equipment = await GetEquipmentAsync(client, equipmentId);
            Assert.Equal(EquipmentStatus.Assigned, equipment.Status);
            Assert.NotNull(equipment.CurrentCustody);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(carrier.EquipmentIds.Count, await dbContext.EquipmentAssignments.CountAsync(
                a => a.EquipmentHandoverId == carrier.HandoverId && a.Status == AssignmentStatus.Active));
        }

        var terms = await ListTermsAsync(client, carrier.HandoverId);
        var current = Assert.Single(terms);
        Assert.Equal(term.Id, current.Id);
        Assert.Equal(1, current.Version);
        Assert.Equal(ResponsibilityTermStatus.Generated, current.Status);
        Assert.Equal(term.Sha256, current.Sha256);
        var bytesAfter = await DownloadAsync(client, term.Id);
        Assert.Equal(bytesBefore, bytesAfter);

        // The same prepared document: one consultant signature, one blank
        // reception date, no device-level signature pairs, no DeliveredOn.
        var text = ExtractShownText(bytesAfter);
        Assert.Equal(1, CountOccurrences(text, @"Assinaturado\(a\)Consultor\(a\):"));
        Assert.Equal(1, CountOccurrences(text, "Datadereceçãodosequipamentos:"));
        Assert.Equal(1, CountOccurrences(text, "____/____/______"));
        var dates = Regex.Count(text, @"\d{2}/\d{2}/\d{4}");
        Assert.Equal(dates, Regex.Count(text, @"(Documentogeradoem|Registadasem:)\d{2}/\d{2}/\d{4}"));
        AssertNoObsoleteSignatureFields(text);
    }

    /// <summary>
    /// The device forms' old "Entregue / Recebido / Devolvido — Assinatura e
    /// Data" fields. Colon-terminated labels only, so ordinary prose such as
    /// "foi entregue em" never matches.
    /// </summary>
    private static void AssertNoObsoleteSignatureFields(string text)
    {
        foreach (var label in new[] { "Entregue:", "Recebido:", "Devolvido:", "AssinaturaeData" })
        {
            Assert.DoesNotContain(label, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AfterConfirmation_ThePreparedTermIsSent_Signed_AndTheHandoverCompletes()
    {
        using var client = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(client);
        var term = await GenerateSuccessfullyAsync(client, carrier.HandoverId);
        await ConfirmDeliveryAsync(client, carrier.HandoverId);

        var send = await Phase7TestData.SendAsync(client, term.Id);
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        var recipient = (await GetHandoverAsync(client, carrier.HandoverId)).ConsultantSnapshot!.Email;
        Assert.Contains(GetEmailSender().SentMessages, m => m.ToAddress == recipient && m.Attachment is not null);
        Assert.Equal(HandoverOperationalStatus.AwaitingSignedTerm, (await GetHandoverAsync(client, carrier.HandoverId)).OperationalStatus);

        var upload = await Phase7TestData.UploadSignedDocumentAsync(client, term.Id, signedOn: DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        Assert.Equal(HandoverOperationalStatus.Completed, (await GetHandoverAsync(client, carrier.HandoverId)).OperationalStatus);
        Assert.Equal(term.Id, Assert.Single(await ListTermsAsync(client, carrier.HandoverId)).Id);
    }

    [Fact]
    public async Task ASignedSupersededVersion_NeverCompletesTheHandover()
    {
        using var client = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(client);
        var v1 = await GenerateSuccessfullyAsync(client, carrier.HandoverId);
        await ConfirmDeliveryAsync(client, carrier.HandoverId);

        // An explicit regeneration after confirmation supersedes v1, as before.
        var v2 = await GenerateSuccessfullyAsync(client, carrier.HandoverId);
        Assert.Equal(2, v2.Version);
        Assert.Equal(ResponsibilityTermStatus.Superseded, (await ListTermsAsync(client, carrier.HandoverId)).Single(t => t.Id == v1.Id).Status);

        (await Phase7TestData.UploadSignedDocumentAsync(client, v1.Id)).EnsureSuccessStatusCode();
        Assert.Equal(HandoverOperationalStatus.AwaitingSignedTerm, (await GetHandoverAsync(client, carrier.HandoverId)).OperationalStatus);

        (await Phase7TestData.UploadSignedDocumentAsync(client, v2.Id)).EnsureSuccessStatusCode();
        Assert.Equal(HandoverOperationalStatus.Completed, (await GetHandoverAsync(client, carrier.HandoverId)).OperationalStatus);
    }

    [Fact]
    public async Task RegeneratingBeforeConfirmation_SupersedesV1_AndConfirmationKeepsV2Current()
    {
        using var client = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(client);
        var v1 = await GenerateSuccessfullyAsync(client, carrier.HandoverId);
        var v2 = await GenerateSuccessfullyAsync(client, carrier.HandoverId);

        await ConfirmDeliveryAsync(client, carrier.HandoverId);

        var terms = await ListTermsAsync(client, carrier.HandoverId);
        Assert.Equal(2, terms.Count);
        Assert.Equal(ResponsibilityTermStatus.Superseded, terms.Single(t => t.Id == v1.Id).Status);
        var current = terms.Single(t => t.Status == ResponsibilityTermStatus.Generated);
        Assert.Equal(v2.Id, current.Id);
        Assert.Equal(v2.Sha256, current.Sha256);
    }

    [Fact]
    public async Task InPersonFlow_IsUnchanged_FreezesAtConfirmation_ThenGeneratesSendsAndSigns()
    {
        using var client = await LoginAsync("HR");
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var type = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        var laptop = await Phase3TestData.CreateLaptopEquipmentAsync(client, type.Id);
        var (handover, _) = await Phase4TestData.PrepareReadyToFinalizeAsync(client, consultantId, laptop.Id, photoCount: 0);

        Assert.Null((await GetHandoverAsync(client, handover.Id)).ConsultantSnapshot);
        await ConfirmDeliveryAsync(client, handover.Id);
        var finalized = await GetHandoverAsync(client, handover.Id);
        Assert.NotNull(finalized.ConsultantSnapshot);
        Assert.Equal(EquipmentStatus.Assigned, (await GetEquipmentAsync(client, laptop.Id)).Status);
        Assert.Equal(await GetCurrentPhotoSetIdAsync(laptop.Id), await GetFrozenPhotoSetIdAsync(handover.Id, laptop.Id));

        var term = await GenerateSuccessfullyAsync(client, handover.Id);
        Assert.Equal(1, CountOccurrences(ExtractShownText(await DownloadAsync(client, term.Id)), "Datadereceçãodosequipamentos:"));
        Assert.Equal(HttpStatusCode.OK, (await Phase7TestData.SendAsync(client, term.Id)).StatusCode);
        (await Phase7TestData.UploadSignedDocumentAsync(client, term.Id)).EnsureSuccessStatusCode();
        Assert.Equal(HandoverOperationalStatus.Completed, (await GetHandoverAsync(client, handover.Id)).OperationalStatus);
    }

    [Fact]
    public async Task PreDeliveryGenerateSendAndUpload_RejectAnonymous_MissingCsrf_AndUsersWithoutThePermission()
    {
        using var hrClient = await LoginAsync("HR");
        var carrier = await CreateCarrierDraftAsync(hrClient);
        var term = await GenerateSuccessfullyAsync(hrClient, carrier.HandoverId);

        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/api/equipment-handovers/{carrier.HandoverId}/responsibility-terms", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/responsibility-terms/{term.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await hrClient.PostAsync($"/api/equipment-handovers/{carrier.HandoverId}/responsibility-terms", null)).StatusCode);

        var (email, password) = await ApiTestUsers.CreateAsync(factory, "HR");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.True((await userManager.RemoveFromRoleAsync(user!, "HR")).Succeeded);
        }

        using var noRoleClient = factory.CreateClient();
        await noRoleClient.LoginAsync(email, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await GenerateAsync(noRoleClient, carrier.HandoverId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Phase7TestData.SendAsync(noRoleClient, term.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Phase7TestData.UploadSignedDocumentAsync(noRoleClient, term.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noRoleClient.GetAsync($"/api/responsibility-terms/{term.Id}/content")).StatusCode);

        Assert.Single(await ListTermsAsync(hrClient, carrier.HandoverId));
    }

    // --- Setup ---------------------------------------------------------------

    private sealed record CarrierDraft(
        Guid HandoverId, Guid ConsultantId, Guid LaptopId, Guid PhoneId, Guid GenericId, IReadOnlyList<string> AssetTags)
    {
        public IReadOnlyList<Guid> EquipmentIds => [LaptopId, PhoneId, GenericId];
    }

    /// <summary>A carrier Draft with a laptop, a mobile phone and a generic item, every inspection recorded (unless told otherwise), dispatched (unless told otherwise).</summary>
    private static async Task<CarrierDraft> CreateCarrierDraftAsync(HttpClient client, bool dispatch = true, bool inspectGeneric = true)
    {
        var (consultantId, _) = await Phase2TestData.CreateConsultantAsync(client);
        var laptopType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "Laptop", assetTagPrefix: "LP");
        var phoneType = await Phase3TestData.CreateEquipmentTypeAsync(client, kind: "MobilePhone", assetTagPrefix: "TM");
        var genericType = await Phase3TestData.CreateEquipmentTypeAsync(client);
        var laptop = await Phase3TestData.CreateLaptopEquipmentAsync(client, laptopType.Id);
        var phone = await Phase3TestData.CreateMobilePhoneEquipmentAsync(client, phoneType.Id);
        var generic = await Phase3TestData.CreateGenericEquipmentAsync(client, genericType.Id);

        var draft = await Phase4TestData.CreateDraftHandoverAsync(client, consultantId, deliveryMethod: "Carrier");
        EquipmentHandoverDetailResponse withItems = draft;
        foreach (var equipmentId in new[] { laptop.Id, phone.Id, generic.Id })
        {
            var add = await Phase4TestData.AddItemAsync(client, draft.Id, equipmentId);
            add.EnsureSuccessStatusCode();
            withItems = (await add.Content.ReadFromJsonAsync<EquipmentHandoverDetailResponse>(HttpClientTestExtensions.JsonOptions))!;
        }

        foreach (var item in withItems.Items.Where(i => inspectGeneric || i.EquipmentId != generic.Id))
        {
            (await Phase4TestData.RecordInspectionAsync(client, draft.Id, item.Id)).EnsureSuccessStatusCode();
        }

        if (dispatch)
        {
            (await Phase4TestData.RegisterDispatchAsync(client, draft.Id)).EnsureSuccessStatusCode();
        }

        return new CarrierDraft(draft.Id, consultantId, laptop.Id, phone.Id, generic.Id, [laptop.AssetTag, phone.AssetTag, generic.AssetTag]);
    }

    private static async Task ConfirmDeliveryAsync(HttpClient client, Guid handoverId)
    {
        var detail = await GetHandoverAsync(client, handoverId);
        if (detail.DeliveredOn is null)
        {
            (await Phase4TestData.SetDeliveredOnAsync(client, handoverId, DateOnly.FromDateTime(DateTime.UtcNow))).EnsureSuccessStatusCode();
        }

        (await Phase4TestData.FinalizeAsync(client, handoverId)).EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> GenerateAsync(HttpClient client, Guid handoverId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync<object?>($"/api/equipment-handovers/{handoverId}/responsibility-terms", null, csrfToken);
    }

    private static async Task<ResponsibilityTermResponse> GenerateSuccessfullyAsync(HttpClient client, Guid handoverId)
    {
        var response = await GenerateAsync(client, handoverId);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ResponsibilityTermResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    private static async Task<IReadOnlyList<ResponsibilityTermResponse>> ListTermsAsync(HttpClient client, Guid handoverId) =>
        (await client.GetFromJsonAsync<List<ResponsibilityTermResponse>>(
            $"/api/equipment-handovers/{handoverId}/responsibility-terms", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<byte[]> DownloadAsync(HttpClient client, Guid termId)
    {
        var response = await client.GetAsync($"/api/responsibility-terms/{termId}/content");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static async Task<EquipmentHandoverDetailResponse> GetHandoverAsync(HttpClient client, Guid handoverId) =>
        (await client.GetFromJsonAsync<EquipmentHandoverDetailResponse>($"/api/equipment-handovers/{handoverId}", HttpClientTestExtensions.JsonOptions))!;

    private static async Task<EquipmentDetailResponse> GetEquipmentAsync(HttpClient client, Guid equipmentId) =>
        (await client.GetFromJsonAsync<EquipmentDetailResponse>($"/api/equipment/{equipmentId}", HttpClientTestExtensions.JsonOptions))!;

    private async Task<Guid?> GetCurrentPhotoSetIdAsync(Guid equipmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.EquipmentPhotoSets
            .Where(s => s.EquipmentId == equipmentId && s.Status == PhotoSetStatus.Current)
            .Select(s => (Guid?)s.Id)
            .SingleOrDefaultAsync();
    }

    private async Task<Guid?> GetFrozenPhotoSetIdAsync(Guid handoverId, Guid equipmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.EquipmentHandoverItems
            .Where(i => i.EquipmentHandoverId == handoverId && i.EquipmentId == equipmentId)
            .Select(i => i.EquipmentPhotoSetId)
            .SingleAsync();
    }

    /// <summary>Marks the equipment's Current set Historical and records a brand-new Current one (rows only — what a later Return's photos would leave behind).</summary>
    private async Task<Guid> ReplaceCurrentPhotoSetAsync(Guid equipmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var current = await dbContext.EquipmentPhotoSets.SingleAsync(s => s.EquipmentId == equipmentId && s.Status == PhotoSetStatus.Current);
        current.Status = PhotoSetStatus.Historical;
        await dbContext.SaveChangesAsync();

        var replacement = new EquipmentPhotoSet
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            Status = PhotoSetStatus.Current,
            Source = current.Source,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = current.CreatedByUserId,
            CreatedByDisplayNameSnapshot = current.CreatedByDisplayNameSnapshot,
        };
        dbContext.EquipmentPhotoSets.Add(replacement);
        await dbContext.SaveChangesAsync();
        return replacement.Id;
    }

    private RecordingEmailSender GetEmailSender() => (RecordingEmailSender)factory.Services.GetRequiredService<IEmailSender>();

    private async Task<HttpClient> LoginAsync(string role)
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, role);
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }

    // --- PDF text ------------------------------------------------------------

    /// <summary>
    /// The text the renderer actually drew: every Flate content stream's
    /// "Tj" string literals, concatenated (words are positioned, so spaces
    /// vanish; parentheses stay escaped). Same technique, and the same
    /// simple-font caveat, as the unit tests' ResponsibilityTermPdfGeneratorTests.
    /// </summary>
    private static string ExtractShownText(byte[] pdfBytes)
    {
        var raw = Encoding.Latin1.GetString(pdfBytes);
        var builder = new StringBuilder();
        foreach (Match streamStart in Regex.Matches(raw, @"stream\r?\n"))
        {
            var start = streamStart.Index + streamStart.Length;
            var end = raw.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0)
            {
                continue;
            }

            var streamBytes = pdfBytes[start..end];
            while (streamBytes.Length > 0 && streamBytes[^1] is (byte)'\n' or (byte)'\r')
            {
                streamBytes = streamBytes[..^1];
            }

            try
            {
                using var compressed = new MemoryStream(streamBytes) { Position = 2 };
                using var inflate = new DeflateStream(compressed, CompressionMode.Decompress);
                using var decompressed = new MemoryStream();
                inflate.CopyTo(decompressed);
                var text = Encoding.Latin1.GetString(decompressed.ToArray());
                foreach (Match literal in Regex.Matches(text, @"\(((?:[^()\\]|\\.)*)\)\s*Tj"))
                {
                    builder.Append(literal.Groups[1].Value);
                }
            }
            catch (InvalidDataException)
            {
                // Not a Flate content stream (e.g. image data).
            }
        }

        return builder.ToString();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
