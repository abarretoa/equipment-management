namespace EquipmentManagement.Domain.Files;

/// <summary>
/// Detects whether an upload is actually a PDF from its magic bytes, never
/// trusting the caller-declared Content-Type or file extension alone
/// (docs/PROJECT_SPEC.md, section 22; Phase 7 scope). Mirrors
/// <see cref="ImageSignatureDetector"/>'s approach for the same reason: a
/// mismatched or lying declaration must be rejected regardless of what the
/// client claimed. Framework-free and unit-testable in isolation. Does not
/// parse or render the PDF — only inspects its header/trailer bytes, so it
/// never executes embedded PDF content (Phase 7 scope).
/// </summary>
public static class PdfSignatureDetector
{
    private static readonly byte[] Header = "%PDF-"u8.ToArray();
    private static readonly byte[] Trailer = "%%EOF"u8.ToArray();

    /// <summary>
    /// True when <paramref name="content"/> starts with the PDF header
    /// (<c>%PDF-</c>) and contains an <c>%%EOF</c> marker within the last
    /// 2048 bytes — the standard location a conforming PDF writer places it.
    /// This is a structural sanity check, not a full PDF parse (Phase 7 scope: the system stores and streams the file; it must not
    /// render it merely to validate it).
    /// </summary>
    public static bool IsPdf(ReadOnlySpan<byte> content)
    {
        if (content.Length < Header.Length || !content[..Header.Length].SequenceEqual(Header))
        {
            return false;
        }

        var tailStart = Math.Max(0, content.Length - 2048);
        var tail = content[tailStart..];
        return tail.IndexOf(Trailer) >= 0;
    }
}
