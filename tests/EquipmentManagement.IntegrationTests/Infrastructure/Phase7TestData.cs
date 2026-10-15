namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Sends/uploads through the real HTTP API for Phase 7 (Responsibility Term
/// delivery/signature workflow) integration test setup — same rationale as
/// <see cref="Phase4TestData"/>.
/// </summary>
public static class Phase7TestData
{
    /// <summary>The smallest byte sequence <see cref="EquipmentManagement.Domain.Files.PdfSignatureDetector"/> recognizes as a genuine PDF.</summary>
    public static readonly byte[] MinimalPdfBytes =
        "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF"u8.ToArray();

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, Guid termId, string? recipientEmail = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        return await client.PostWithCsrfAsync($"/api/responsibility-terms/{termId}/send", new { RecipientEmail = recipientEmail }, csrfToken);
    }

    public static async Task<HttpResponseMessage> UploadSignedDocumentAsync(
        HttpClient client, Guid termId, byte[]? bytes = null, string fileName = "signed.pdf",
        string contentType = "application/pdf", DateOnly? signedOn = null)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var formFields = signedOn is { } date
            ? new Dictionary<string, string> { ["signedOn"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) }
            : null;

        return await client.PostFileWithCsrfAsync(
            $"/api/responsibility-terms/{termId}/signed-document", "file", bytes ?? MinimalPdfBytes, fileName, contentType, csrfToken, formFields);
    }
}
