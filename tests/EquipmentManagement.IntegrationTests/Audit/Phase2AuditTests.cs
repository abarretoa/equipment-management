using System.Net.Http.Json;
using EquipmentManagement.Api.Contracts;
using EquipmentManagement.Api.Contracts.Audit;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.IntegrationTests.Infrastructure;

namespace EquipmentManagement.IntegrationTests.Audit;

/// <summary>
/// Phase 2 (Company/Consultant/Allocation) audit events, with particular focus
/// on the Consultant PII redaction strategy (Phase 2 scope): NIF/Address/Phone/Email must never appear as
/// values in the audit trail, only as names in a "changedFields" list.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class Phase2AuditTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task CreatingACompany_RecordsAnAuditEvent()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (id, name) = await Phase2TestData.CreateCompanyAsync(client);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=Company&entityId={id}&action=Company.Created");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        var entry = Assert.Single(page!.Items);
        Assert.Contains(name, entry.NewValuesJson);
    }

    [Fact]
    public async Task CreatingAConsultant_RecordsAnAuditEventWithoutNifAddressPhoneOrEmail()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var nif = Random.Shared.NextInt64(100000000, 999999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var (id, _) = await Phase2TestData.CreateConsultantAsync(client, fullName: "Carlos Demo", nif: nif);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=Consultant&entityId={id}&action=Consultant.Created");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        var entry = Assert.Single(page!.Items);
        Assert.Contains("Carlos Demo", entry.NewValuesJson);
        Assert.DoesNotContain(nif, entry.NewValuesJson ?? string.Empty);
        Assert.DoesNotContain(nif, entry.MetadataJson ?? string.Empty);
    }

    [Fact]
    public async Task UpdatingAConsultant_RecordsChangedFieldNamesButNeverSensitiveValues()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (id, oldNif) = await Phase2TestData.CreateConsultantAsync(client);
        var newNif = Random.Shared.NextInt64(100000000, 999999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        const string newAddress = "Rua Segredo 42, Porto";
        const string newPhone = "+351911223344";

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        await client.PutWithCsrfAsync($"/api/consultants/{id}", new
        {
            FullName = "Test Consultant",
            Nif = newNif,
            Email = $"changed.{Guid.NewGuid():N}@example.invalid",
            Address = newAddress,
            Phone = newPhone,
        }, csrfToken);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=Consultant&entityId={id}&action=Consultant.Updated");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        var entry = Assert.Single(page!.Items);
        var metadata = entry.MetadataJson ?? string.Empty;

        Assert.Contains("changedFields", metadata);
        Assert.Contains("nif", metadata);
        Assert.Contains("address", metadata);
        Assert.Contains("phone", metadata);
        Assert.Contains("email", metadata);

        // The actual sensitive values must never appear anywhere in the record.
        Assert.DoesNotContain(oldNif, metadata);
        Assert.DoesNotContain(newNif, metadata);
        Assert.DoesNotContain(newAddress, metadata);
        Assert.DoesNotContain(newPhone, metadata);
        Assert.DoesNotContain(oldNif, entry.OldValuesJson ?? string.Empty);
        Assert.DoesNotContain(newNif, entry.NewValuesJson ?? string.Empty);
    }

    [Fact]
    public async Task DeactivatingAConsultant_RecordsAnAuditEvent()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (id, _) = await Phase2TestData.CreateConsultantAsync(client);

        var csrfToken = await client.GetAntiforgeryTokenAsync();
        await client.PostWithCsrfAsync<object?>($"/api/consultants/{id}/deactivate", null, csrfToken);

        var auditResponse = await client.GetAsync($"/api/audit?entityType=Consultant&entityId={id}&action=Consultant.Deactivated");
        var page = await auditResponse.Content.ReadFromJsonAsync<PagedResponse<AuditLogResponse>>(HttpClientTestExtensions.JsonOptions);

        Assert.Contains(page!.Items, e => e.Action == "Consultant.Deactivated");
    }

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }
}
