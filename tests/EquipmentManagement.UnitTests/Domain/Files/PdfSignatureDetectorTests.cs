using EquipmentManagement.Domain.Files;

namespace EquipmentManagement.UnitTests.Domain.Files;

public class PdfSignatureDetectorTests
{
    private static byte[] BuildMinimalPdf() =>
        "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF"u8.ToArray();

    [Fact]
    public void IsPdf_TrueForGenuinePdf()
    {
        Assert.True(PdfSignatureDetector.IsPdf(BuildMinimalPdf()));
    }

    [Fact]
    public void IsPdf_FalseWhenHeaderMissing()
    {
        var bytes = "not a pdf at all, just plain text\n%%EOF"u8.ToArray();
        Assert.False(PdfSignatureDetector.IsPdf(bytes));
    }

    [Fact]
    public void IsPdf_FalseWhenTrailerMissing()
    {
        var bytes = "%PDF-1.7\nsome content with no trailer marker"u8.ToArray();
        Assert.False(PdfSignatureDetector.IsPdf(bytes));
    }

    [Fact]
    public void IsPdf_FalseForTooShortInput()
    {
        Assert.False(PdfSignatureDetector.IsPdf([0x25, 0x50]));
    }

    [Fact]
    public void IsPdf_FalseForRenamedNonPdfFile()
    {
        // A .docx/.jpg renamed to .pdf — exactly the scenario magic-byte
        // detection exists to catch (Phase 7 scope).
        var fakeBytes = "this is actually a plain text file pretending to be a pdf"u8.ToArray();
        Assert.False(PdfSignatureDetector.IsPdf(fakeBytes));
    }

    [Fact]
    public void IsPdf_FindsTrailerNearEndOfLargeFile()
    {
        var padding = new byte[5000];
        Array.Fill(padding, (byte)'x');
        var header = "%PDF-1.7\n"u8.ToArray();
        var trailer = "\n%%EOF"u8.ToArray();
        var bytes = header.Concat(padding).Concat(trailer).ToArray();

        Assert.True(PdfSignatureDetector.IsPdf(bytes));
    }

    [Fact]
    public void IsPdf_FalseWhenTrailerIsTooFarFromEnd()
    {
        var trailer = "%%EOF"u8.ToArray();
        var padding = new byte[5000];
        Array.Fill(padding, (byte)'x');
        var header = "%PDF-1.7\n"u8.ToArray();
        var bytes = header.Concat(trailer).Concat(padding).ToArray();

        Assert.False(PdfSignatureDetector.IsPdf(bytes));
    }
}
