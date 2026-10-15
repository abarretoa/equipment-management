using ClosedXML.Excel;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Infrastructure.DataImports;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

public sealed record ConsultantImportRow(
    string FullName, string Nif, string Email, string Phone, string Address,
    string? WorkforceType = null, string? EngagementType = null, string? RoleOrFunction = null, string? Company = null,
    DateOnly? AllocationStartDate = null, DateOnly? AllocationEndDate = null, bool NifAsNumber = false, bool FullNameAsFormula = false);

public sealed record EquipmentImportRow(
    string EquipmentType, string? Manufacturer = null, string? Model = null, string? SerialNumber = null,
    string? Notes = null, string? Processor = null, string? Memory = null, string? Storage = null, bool SerialAsNumber = false);

/// <summary>
/// Builds real in-memory .xlsx test fixtures via ClosedXML — matches Phase 8 scope's "use real .xlsx bytes generated in tests, not only mocks."
/// By default a workbook is written in the legacy English format (sheet
/// name and headers of the templates generated before the pt-PT
/// localization), so every test that does not ask otherwise keeps proving
/// that format is still accepted; pass <c>headers</c> for any other header row.
/// </summary>
public static class Phase8TestData
{
    /// <summary>The header row of the pre-localization Consultant template.</summary>
    public static readonly string[] LegacyConsultantHeaders =
        ["FullName", "NIF", "Email", "Phone", "Address", "WorkforceType", "EngagementType", "RoleOrFunction", "Company", "AllocationStartDate", "AllocationEndDate"];

    /// <summary>The header row of the pre-localization Equipment template.</summary>
    public static readonly string[] LegacyEquipmentHeaders = ["EquipmentType", "Manufacturer", "Model", "SerialNumber", "Notes", "Processor", "Memory", "Storage"];

    /// <summary>The header row of the current (pt-PT) Consultant template.</summary>
    public static string[] PortugueseConsultantHeaders => [.. ConsultantImportColumns.All.Select(c => c.Header)];

    /// <summary>The header row of the current (pt-PT) Equipment template.</summary>
    public static string[] PortugueseEquipmentHeaders => [.. EquipmentImportColumns.All.Select(c => c.Header)];

    public static byte[] BuildConsultantWorkbook(IEnumerable<ConsultantImportRow> rows, string sheetName = "Consultants", string[]? headers = null)
    {
        headers ??= LegacyConsultantHeaders;
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).SetValue(headers[i]);
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            if (row.NifAsNumber)
            {
                sheet.Cell(rowIndex, 2).SetValue(long.Parse(row.Nif, System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                var cell = sheet.Cell(rowIndex, 2);
                cell.SetValue(row.Nif);
                cell.Style.NumberFormat.Format = "@";
            }

            if (row.FullNameAsFormula)
            {
                sheet.Cell(rowIndex, 1).FormulaA1 = "=1+1";
            }
            else
            {
                sheet.Cell(rowIndex, 1).SetValue(row.FullName);
            }

            sheet.Cell(rowIndex, 3).SetValue(row.Email);
            sheet.Cell(rowIndex, 4).SetValue(row.Phone);
            sheet.Cell(rowIndex, 5).SetValue(row.Address);
            sheet.Cell(rowIndex, 6).SetValue(row.WorkforceType ?? string.Empty);
            sheet.Cell(rowIndex, 7).SetValue(row.EngagementType ?? string.Empty);
            sheet.Cell(rowIndex, 8).SetValue(row.RoleOrFunction ?? string.Empty);
            sheet.Cell(rowIndex, 9).SetValue(row.Company ?? string.Empty);
            if (row.AllocationStartDate is { } start)
            {
                sheet.Cell(rowIndex, 10).SetValue(start.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (row.AllocationEndDate is { } end)
            {
                sheet.Cell(rowIndex, 11).SetValue(end.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture));
            }

            rowIndex++;
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    public static byte[] BuildEquipmentWorkbook(IEnumerable<EquipmentImportRow> rows, string sheetName = "Equipment", string[]? headers = null)
    {
        headers ??= LegacyEquipmentHeaders;
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).SetValue(headers[i]);
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).SetValue(row.EquipmentType);
            sheet.Cell(rowIndex, 2).SetValue(row.Manufacturer ?? string.Empty);
            sheet.Cell(rowIndex, 3).SetValue(row.Model ?? string.Empty);

            if (row.SerialAsNumber && row.SerialNumber is not null)
            {
                sheet.Cell(rowIndex, 4).SetValue(long.Parse(row.SerialNumber, System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                var cell = sheet.Cell(rowIndex, 4);
                cell.SetValue(row.SerialNumber ?? string.Empty);
                cell.Style.NumberFormat.Format = "@";
            }

            sheet.Cell(rowIndex, 5).SetValue(row.Notes ?? string.Empty);
            sheet.Cell(rowIndex, 6).SetValue(row.Processor ?? string.Empty);
            sheet.Cell(rowIndex, 7).SetValue(row.Memory ?? string.Empty);
            sheet.Cell(rowIndex, 8).SetValue(row.Storage ?? string.Empty);
            rowIndex++;
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    public static Task<HttpResponseMessage> PreviewConsultantsAsync(HttpClient client, byte[] bytes, string fileName = "consultants.xlsx") =>
        UploadAsync(client, "/api/imports/consultants/preview", bytes, fileName);

    public static Task<HttpResponseMessage> PreviewEquipmentAsync(HttpClient client, byte[] bytes, string fileName = "equipment.xlsx") =>
        UploadAsync(client, "/api/imports/equipment/preview", bytes, fileName);

    public static async Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string url)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync<object?>(url, null, csrfToken);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string url, byte[] bytes, string fileName)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostFileWithCsrfAsync(
            url, "file", bytes, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", csrfToken);
    }
}
