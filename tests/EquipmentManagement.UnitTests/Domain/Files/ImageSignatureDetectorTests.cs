using EquipmentManagement.Domain.Files;

namespace EquipmentManagement.UnitTests.Domain.Files;

public class ImageSignatureDetectorTests
{
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] WebPHeader = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x00, 0x00, 0x00, 0x00, (byte)'W', (byte)'E', (byte)'B', (byte)'P'];
    private static readonly byte[] GifHeader = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a'];

    [Fact]
    public void Detect_RecognizesJpeg()
    {
        Assert.Equal(ImageContentType.Jpeg, ImageSignatureDetector.Detect(JpegHeader));
    }

    [Fact]
    public void Detect_RecognizesPng()
    {
        Assert.Equal(ImageContentType.Png, ImageSignatureDetector.Detect(PngHeader));
    }

    [Fact]
    public void Detect_RecognizesWebP()
    {
        Assert.Equal(ImageContentType.WebP, ImageSignatureDetector.Detect(WebPHeader));
    }

    [Fact]
    public void Detect_ReturnsNullForUnsupportedFormat()
    {
        Assert.Null(ImageSignatureDetector.Detect(GifHeader));
    }

    [Fact]
    public void Detect_ReturnsNullForTooShortInput()
    {
        Assert.Null(ImageSignatureDetector.Detect([0xFF, 0xD8]));
    }

    [Fact]
    public void Detect_ReturnsNullWhenBytesAreRenamedNonImageFile()
    {
        // A .txt file renamed to .jpg — this is exactly the scenario magic-byte
        // detection exists to catch (docs/PROJECT_SPEC.md, section 22).
        var fakeBytes = "not a real image"u8.ToArray();
        Assert.Null(ImageSignatureDetector.Detect(fakeBytes));
    }

    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/jpg", true)]
    [InlineData("IMAGE/JPEG", true)]
    [InlineData("image/png", false)]
    [InlineData("application/octet-stream", false)]
    public void MatchesDeclaredContentType_JpegOnlyMatchesJpegDeclarations(string declared, bool expected)
    {
        Assert.Equal(expected, ImageSignatureDetector.MatchesDeclaredContentType(ImageContentType.Jpeg, declared));
    }

    [Fact]
    public void MatchesDeclaredContentType_MismatchedDeclarationIsRejected()
    {
        // The declared Content-Type must match what the bytes actually are —
        // a PNG uploaded with a "image/jpeg" header must not pass.
        Assert.False(ImageSignatureDetector.MatchesDeclaredContentType(ImageContentType.Png, "image/jpeg"));
    }
}
