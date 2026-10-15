using System.Reflection;
using PdfSharp.Fonts;

namespace EquipmentManagement.Infrastructure.Documents;

/// <summary>
/// Resolves every font request to the embedded Liberation Sans family
/// (Regular/Bold), so PDF generation never depends on fonts installed on the
/// host machine (Phase 6 scope) — required for the Docker/
/// Linux deployment target, since PDFsharp's cross-platform package has no
/// implicit OS font access. Liberation Sans is licensed under the SIL Open
/// Font License 1.1 (see <c>Documents/Fonts/LICENSE-LiberationFonts-OFL-1.1.txt</c>),
/// which explicitly permits free embedding/redistribution, including in
/// commercial closed-source software — chosen deliberately over PDFsharp's
/// own bundled <c>PdfSharp.WPFonts</c> package, which ships Microsoft's
/// "Segoe WP" under terms not clearly permissive for this kind of embedding.
/// Registered once via <see cref="Register"/> at startup
/// (<c>DependencyInjection.AddInfrastructure</c>).
/// </summary>
public sealed class LiberationFontResolver : IFontResolver
{
    public const string FamilyName = "Liberation Sans";

    private const string RegularFace = "LiberationSans#Regular";
    private const string BoldFace = "LiberationSans#Bold";

    private static readonly Lazy<byte[]> RegularBytes = new(() => LoadEmbeddedFont("LiberationSans-Regular.ttf"));
    private static readonly Lazy<byte[]> BoldBytes = new(() => LoadEmbeddedFont("LiberationSans-Bold.ttf"));

    public static void Register()
    {
        if (GlobalFontSettings.FontResolver is null)
        {
            GlobalFontSettings.FontResolver = new LiberationFontResolver();
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        // Italic is not distinguished — this document never uses italic text,
        // and Liberation Sans's regular/bold faces are enough for it. If a
        // future template needs italics, add the Italic/BoldItalic embedded
        // font files and branch here rather than silently substituting.
        new FontResolverInfo(bold ? BoldFace : RegularFace);

    public byte[] GetFont(string faceName) => faceName switch
    {
        BoldFace => BoldBytes.Value,
        _ => RegularBytes.Value,
    };

    private static byte[] LoadEmbeddedFont(string fileName)
    {
        var assembly = typeof(LiberationFontResolver).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Documents.Fonts.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' was not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
