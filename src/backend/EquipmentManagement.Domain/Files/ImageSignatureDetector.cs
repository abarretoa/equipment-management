namespace EquipmentManagement.Domain.Files;

/// <summary>
/// Detects an image's real format from its magic bytes, never trusting the
/// caller-declared Content-Type or file extension alone (docs/PROJECT_SPEC.md,
/// section 22: "validação de upload... restrição de tamanho e MIME type").
/// Framework-free and unit-testable in isolation.
/// </summary>
public static class ImageSignatureDetector
{
    public static ImageContentType? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ImageContentType.Jpeg;
        }

        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return ImageContentType.Png;
        }

        if (header.Length >= 12 &&
            header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
            header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            return ImageContentType.WebP;
        }

        return null;
    }

    /// <summary>The declared Content-Type must match what the bytes actually are — a mismatch is rejected outright.</summary>
    public static bool MatchesDeclaredContentType(ImageContentType detected, string declaredContentType) =>
        declaredContentType.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => detected == ImageContentType.Jpeg,
            "image/png" => detected == ImageContentType.Png,
            "image/webp" => detected == ImageContentType.WebP,
            _ => false,
        };
}
