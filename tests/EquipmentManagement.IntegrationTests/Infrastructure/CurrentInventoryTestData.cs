using System.Globalization;
using System.Net.Http.Json;
using ClosedXML.Excel;
using EquipmentManagement.Api.Contracts.Consultants;
using EquipmentManagement.Api.Contracts.DataImports;
using EquipmentManagement.Infrastructure.DataImports;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>One person (and their current allocation) as written on a Current Inventory row — synthetic data only.</summary>
public sealed record InventoryPerson(
    string FullName,
    string Nif,
    string Email,
    string Phone = "+351900000000",
    string Address = "Rua de Teste 1, 1000-001 Lisboa",
    string? Regime = "Interno",
    string? EngagementType = "Contrato de trabalho",
    string? Role = "Consultor de sistemas",
    string? Company = null,
    DateOnly? Start = null,
    DateOnly? End = null)
{
    public static InventoryPerson NewInternal(string? fullName = null) => new(
        fullName ?? "Pessoa Interna Teste", NewNif(), NewEmail(), Start: new DateOnly(2025, 1, 6));

    public static InventoryPerson NewExternal(string company, string? fullName = null) => new(
        fullName ?? "Pessoa Externa Teste", NewNif(), NewEmail(), Regime: "Externo", EngagementType: "Prestador de serviço",
        Company: company, Start: new DateOnly(2025, 3, 3));

    public static string NewNif() => Random.Shared.NextInt64(100000000, 999999999).ToString(CultureInfo.InvariantCulture);

    public static string NewEmail() => $"inventario.{Guid.NewGuid():N}@example.invalid";
}

/// <summary>One Current Inventory row: one piece of equipment, optionally held by <see cref="Person"/>.</summary>
public sealed record InventoryRow(
    string EquipmentType,
    string? Manufacturer = null,
    string? Model = null,
    string? SerialNumber = null,
    string? Notes = null,
    string? Processor = null,
    string? Memory = null,
    string? Storage = null,
    string? Imei = null,
    string? MobileNumber = null,
    string? SimSerialNumber = null,
    InventoryPerson? Person = null,
    DateOnly? AssignmentDate = null)
{
    public static string NewSerial() => $"CI{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    public static string NewImei() => Random.Shared.NextInt64(100000000000000, 999999999999999).ToString(CultureInfo.InvariantCulture);

    public static InventoryRow Laptop(string type, InventoryPerson? person = null, string? serial = null, DateOnly? assignmentDate = null) =>
        new(type, "Dell", "Latitude 5440", serial ?? NewSerial(), Processor: "Intel Core i5", Memory: "16 GB", Storage: "512 GB SSD",
            Person: person, AssignmentDate: assignmentDate);

    public static InventoryRow Generic(string type, InventoryPerson? person = null, string? serial = null) =>
        new(type, "Logitech", "MX", serial ?? NewSerial(), Person: person);

    public static InventoryRow Phone(string type, InventoryPerson? person = null, string? imei = null) =>
        new(type, "Samsung", "Galaxy A55", Imei: imei ?? NewImei(), MobileNumber: "+351910000000", SimSerialNumber: "8935100000000000001", Person: person);
}

/// <summary>Builds real Current Inventory .xlsx workbooks (pt-PT headers, template column order) and drives the real endpoints.</summary>
public static class CurrentInventoryTestData
{
    public const string BaseUrl = "/api/imports/current-inventory";

    public static byte[] BuildWorkbook(IEnumerable<InventoryRow> rows, Action<IXLWorksheet>? tweak = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Inventário atual");
        var columns = CurrentInventoryImportColumns.All;
        for (var i = 0; i < columns.Count; i++)
        {
            sheet.Cell(1, i + 1).SetValue(columns[i].Header);
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            var p = row.Person;
            string?[] values =
            [
                row.EquipmentType, row.Manufacturer, row.Model, row.SerialNumber, row.Notes, row.Processor, row.Memory, row.Storage,
                row.Imei, row.MobileNumber, row.SimSerialNumber,
                p?.FullName, p?.Nif, p?.Email, p?.Phone, p?.Address, p?.Regime, p?.EngagementType, p?.Role, p?.Company,
                Date(p?.Start), Date(p?.End), Date(row.AssignmentDate),
            ];

            for (var i = 0; i < values.Length; i++)
            {
                var cell = sheet.Cell(rowIndex, i + 1);
                cell.Style.NumberFormat.Format = "@";
                cell.SetValue(values[i] ?? string.Empty);
            }

            rowIndex++;
        }

        tweak?.Invoke(sheet);

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    public static async Task<HttpResponseMessage> PreviewRawAsync(HttpClient client, byte[] bytes, string fileName = "inventario.xlsx")
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostFileWithCsrfAsync(
            $"{BaseUrl}/preview", "file", bytes, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", csrfToken);
    }

    public static async Task<CurrentInventoryImportPreviewResponse> PreviewAsync(HttpClient client, byte[] bytes)
    {
        var response = await PreviewRawAsync(client, bytes);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CurrentInventoryImportPreviewResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    public static async Task<HttpResponseMessage> ConfirmRawAsync(HttpClient client, Guid sessionId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync<object?>($"{BaseUrl}/{sessionId}/confirm", null, csrfToken);
    }

    public static async Task<CurrentInventoryImportConfirmResponse> PreviewAndConfirmAsync(HttpClient client, IEnumerable<InventoryRow> rows)
    {
        var preview = await PreviewAsync(client, BuildWorkbook(rows));
        if (preview.ErrorRows > 0)
        {
            var problems = preview.Rows.SelectMany(r => r.Issues.Select(i => $"row {r.RowNumber}: {i.Code} {i.Message}"));
            throw new InvalidOperationException("Preview unexpectedly has errors: " + string.Join(" | ", problems));
        }

        var response = await ConfirmRawAsync(client, preview.ImportSessionId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CurrentInventoryImportConfirmResponse>(HttpClientTestExtensions.JsonOptions))!;
    }

    /// <summary>Creates a Consultant through the normal API with exactly the person data of <paramref name="person"/>.</summary>
    public static async Task<Guid> CreateConsultantAsync(HttpClient client, InventoryPerson person)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync("/api/consultants", new
        {
            person.FullName,
            person.Nif,
            person.Email,
            person.Address,
            person.Phone,
        }, csrfToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConsultantDetailResponse>(HttpClientTestExtensions.JsonOptions))!.Id;
    }

    /// <summary>Creates the person's Active allocation through the normal API, from the same values the workbook row carries.</summary>
    public static async Task<Guid> CreateAllocationAsync(HttpClient client, Guid consultantId, InventoryPerson person, Guid? companyId)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync($"/api/consultants/{consultantId}/allocations", new
        {
            WorkforceType = person.Regime == "Externo" ? "External" : "Internal",
            EngagementType = person.EngagementType == "Prestador de serviço" ? "ServiceProvider" : "EmploymentContract",
            RoleOrFunction = person.Role,
            CompanyId = companyId,
            StartDate = person.Start,
            EndDate = person.End,
        }, csrfToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EquipmentManagement.Api.Contracts.Allocations.ConsultantAllocationResponse>(HttpClientTestExtensions.JsonOptions))!.Id;
    }

    private static string? Date(DateOnly? value) => value?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
