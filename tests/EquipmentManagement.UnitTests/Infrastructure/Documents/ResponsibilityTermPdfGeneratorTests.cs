using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Documents;
using EquipmentManagement.UnitTests.Infrastructure.ClientProfiles;

namespace EquipmentManagement.UnitTests.Infrastructure.Documents;

/// <summary>
/// Exercises the real PDF renderer (no mocking of <see cref="IResponsibilityTermPdfGenerator"/>
/// here — Phase 6 scope: "at least some tests must exercise the
/// real generator"). Deterministic and network-free: MigraDoc/PDFsharp never
/// touch the filesystem, the network, or OS fonts (the embedded Liberation
/// Sans resolver is used unconditionally).
/// </summary>
public sealed class ResponsibilityTermPdfGeneratorTests
{
    private static readonly DocumentIssuerOptions ConfiguredIssuer = new()
    {
        LegalName = "Synthetic Test Company, Lda.",
        Address = "Rua de Teste 1, 1000-000 Lisboa",
        TaxId = "500000000",
    };

    [Fact]
    public void Generate_ProducesNonEmptyValidPdfBytes()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: true);
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, ConfiguredIssuer);

        Assert.NotEmpty(bytes);
        // A real PDF file always begins with "%PDF-" and ends with "%%EOF".
        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
        var tail = System.Text.Encoding.ASCII.GetString(bytes[^32..]);
        Assert.Contains("%%EOF", tail);
    }

    [Fact]
    public void Generate_WithoutIssuerConfigured_StillProducesAValidPdf()
    {
        // The issuer block is optional in the layout (omitted, never
        // fabricated) — generation itself must not depend on it having been
        // configured; ResponsibilityTermService is what enforces the
        // "issuer must be configured" business rule before ever calling here.
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false);
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, new DocumentIssuerOptions());

        Assert.NotEmpty(bytes);
        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
    }

    [Fact]
    public void Generate_WithLaptopSpecification_DoesNotThrow()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: true, includeProfessionalContext: true);
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, ConfiguredIssuer);

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Generate_WithManyEquipmentItems_ProducesAMultiPageDocument()
    {
        // Enough rows to force the table across at least one page break —
        // proves the table doesn't truncate items to fit one page (Phase 6 scope).
        var model = BuildModel(itemCount: 80, includeLaptop: true, includeProfessionalContext: true);
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, ConfiguredIssuer);

        Assert.NotEmpty(bytes);
        var content = System.Text.Encoding.Latin1.GetString(bytes);
        // A single-page PDF has one "/Type/Page" (or "/Type /Page") object; a
        // multi-page one has several — a coarse but dependency-free way to
        // assert pagination happened without adding a full PDF-parsing library.
        var pageObjectCount = CountOccurrences(content, "/Type/Page") + CountOccurrences(content, "/Type /Page");
        Assert.True(pageObjectCount >= 2, $"Expected a multi-page PDF, found {pageObjectCount} page object(s).");
    }

    [Fact]
    public void Generate_ForTheSameModelAndIssuer_ProducesStableSizedValidOutput()
    {
        // PDF libraries commonly embed a per-render random trailer /ID value
        // (used for incremental-update tracking) even for byte-identical
        // content, so full byte-for-byte equality across two render calls is
        // not a meaningful property to assert — what actually matters for
        // this feature (and is covered by ResponsibilityTermServiceTests at
        // the integration level) is that the SHA-256 recorded for one stored
        // PDF matches that exact file's bytes on every later download, not
        // that two independent renders are identical. Here we only confirm
        // the same input reliably produces a valid, similarly-sized PDF twice.
        var model = BuildModel(itemCount: 2, includeLaptop: true, includeProfessionalContext: true, generatedAtUtc: new DateTime(2026, 9, 12, 14, 22, 0, DateTimeKind.Utc));
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var first = generator.Generate(model, ConfiguredIssuer);
        var second = generator.Generate(model, ConfiguredIssuer);

        Assert.Equal("%PDF-"u8.ToArray(), first[..5]);
        Assert.Equal("%PDF-"u8.ToArray(), second[..5]);
        Assert.InRange(second.Length, first.Length - 64, first.Length + 64);
    }

    [Fact]
    public void Generate_UnsupportedTemplateVersion_Throws()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false) with { TemplateVersion = 999 };
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        Assert.Throws<NotSupportedException>(() => generator.Generate(model, ConfiguredIssuer));
    }

    [Fact]
    public void Generate_WithLongUnbrokenManufacturerAndModelValues_DoesNotThrow()
    {
        // Regression coverage for a real defect found during Phase 6 manual
        // verification: a single unspaced value with no natural wrap point
        // (e.g. a serial number like "SN-ORIGINAL-000000000000001") visually
        // overflowed its table column and overlapped the next one, because
        // MigraDoc only wraps at existing whitespace — confirmed by
        // extracting the actual rendered PDF text and seeing the values
        // merged. The Nº de Série column is fixed by WithBreakOpportunities
        // (see its own tests below and the render-level tests further down);
        // Manufacturer/Model are human-readable text and are deliberately
        // never run through that helper (see AddPlainCellText's remarks) —
        // this test just proves the full render pipeline still produces a
        // valid PDF even for pathological, space-free input in either kind
        // of column.
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false) with
        {
            EquipmentItems =
            [
                new ResponsibilityTermEquipmentItem(
                    AssetTag: "LT-0001",
                    EquipmentTypeName: "Monitor",
                    Manufacturer: "OriginalManufacturerWithNoSpacesAtAll",
                    Model: "OriginalModelX1AlsoWithNoSpaces",
                    SerialNumber: "SN-ORIGINAL-000000000000001",
                    Kind: "Generic",
                    LaptopSpecification: null,
                    MobilePhoneSpecification: null,
                    Notes: null,
                    PhotoSet: null),
            ],
        };
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, ConfiguredIssuer);

        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
    }

    /// <summary>
    /// Matches <c>ResponsibilityTermPdfGenerator</c>'s own invisible break
    /// character (U+200B, zero-width space) — deliberately not U+00AD (soft
    /// hyphen), see that constant's remarks for the empirical finding that
    /// drove the switch: this installed MigraDoc/PDFsharp version always
    /// draws U+00AD as a literal visible hyphen when it is the last character
    /// of a text run, regardless of whether a line-wrap is actually needed
    /// there.
    /// </summary>
    private const char InvisibleBreak = (char)0x200B;

    /// <summary>A genuine, decodable 1x1 transparent PNG — unlike the magic-byte-only fixtures used elsewhere in this codebase's tests (sufficient for signature-sniffing validation but not for a real image decoder), embedding actually requires MigraDoc/PDFsharp to decode real pixel data.</summary>
    private static readonly byte[] MinimalValidPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public void Generate_TemplateVersion1_StillProducesAValidPdf()
    {
        // Version 1 must remain available forever, unchanged, for any
        // historical ResponsibilityTerm generated under it (ADR-048, ADR-079) — this is
        // never re-invoked for new generations (CurrentVersion is 2), but the
        // code path itself must keep working.
        var model = BuildModel(itemCount: 1, includeLaptop: true, includeProfessionalContext: true) with { TemplateVersion = 1 };
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, ConfiguredIssuer);

        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
    }

    private static int CountEmbeddedImageObjects(byte[] pdfBytes)
    {
        // Structural PDF dictionaries (unlike content-stream text) are never
        // compressed, so counting "/Subtype/Image" XObjects is a reliable,
        // dependency-free way to confirm images were embedded, without
        // parsing the PDF or asserting on rendered pixel data. Not usable as
        // an exact count on its own — an image with an alpha channel gets a
        // second XObject for its soft mask — so tests compare counts
        // relatively (with vs. without photos) rather than asserting a fixed
        // absolute number.
        var content = System.Text.Encoding.Latin1.GetString(pdfBytes);
        return CountOccurrences(content, "/Subtype/Image") + CountOccurrences(content, "/Subtype /Image");
    }

    [Fact]
    public void Generate_LaptopWithCurrentPhotoSet_EmbedsMoreImagesThanWithoutOne()
    {
        var photoSet = new ResponsibilityTermPhotoSet(
            new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc), [MinimalValidPngBytes, MinimalValidPngBytes, MinimalValidPngBytes]);
        var withoutPhotos = BuildModel(itemCount: 1, includeLaptop: true, includeProfessionalContext: false);
        var withPhotos = withoutPhotos with
        {
            EquipmentItems =
            [
                withoutPhotos.EquipmentItems[0] with { PhotoSet = photoSet },
            ],
        };
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytesWithout = generator.Generate(withoutPhotos, ConfiguredIssuer);
        var bytesWith = generator.Generate(withPhotos, ConfiguredIssuer);

        Assert.Equal("%PDF-"u8.ToArray(), bytesWith[..5]);
        Assert.True(
            CountEmbeddedImageObjects(bytesWith) > CountEmbeddedImageObjects(bytesWithout),
            "Expected the photo-bearing Term to embed strictly more image objects than the one without a photo set.");
    }

    [Fact]
    public void Generate_LaptopWithNoCurrentPhotoSet_DoesNotThrow()
    {
        // The generator itself stays a pure "renders what it's given" tool —
        // ResponsibilityTermService is what enforces "a Laptop/MobilePhone
        // Term cannot be generated without a photo set" before ever calling
        // here (mirrors the existing "issuer not configured" split of
        // responsibility). A missing PhotoSet must not throw — the photo
        // section is simply omitted (see AddPhotoSection).
        var model = BuildModel(itemCount: 1, includeLaptop: true, includeProfessionalContext: false);
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, ConfiguredIssuer);

        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
    }

    [Fact]
    public void Generate_MobilePhoneWithCurrentPhotoSet_ProducesValidPdf()
    {
        var photoSet = new ResponsibilityTermPhotoSet(
            new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc), [MinimalValidPngBytes, MinimalValidPngBytes, MinimalValidPngBytes]);
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false) with
        {
            EquipmentItems =
            [
                new ResponsibilityTermEquipmentItem(
                    AssetTag: "TM-0001", EquipmentTypeName: "Telemóvel", Manufacturer: "Samsung", Model: "Galaxy A05s",
                    SerialNumber: null, Kind: "MobilePhone", LaptopSpecification: null,
                    MobilePhoneSpecification: new ResponsibilityTermMobilePhoneSpecification("350000000000123", "910000555", "89351000000000000123"),
                    Notes: "Usado c/ carregador", PhotoSet: photoSet),
            ],
        };
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, ConfiguredIssuer);

        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
        Assert.True(CountEmbeddedImageObjects(bytes) > 1, "Expected the mobile phone's 3 condition photos to be embedded alongside the logo.");
    }

    [Fact]
    public void Generate_MixOfGenericLaptopAndMobilePhoneItems_DoesNotThrow()
    {
        var photoSet = new ResponsibilityTermPhotoSet(DateTime.UtcNow, [MinimalValidPngBytes, MinimalValidPngBytes, MinimalValidPngBytes]);
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: true) with
        {
            EquipmentItems =
            [
                new ResponsibilityTermEquipmentItem(
                    "MN-0001", "Monitor", "LG", "27UL", "SN-MON-1", "Generic", null, null, null, null),
                new ResponsibilityTermEquipmentItem(
                    "LT-0001", "Laptop", "Dell", "Latitude 5450", "SN-LT-1", "Laptop",
                    new ResponsibilityTermLaptopSpecification("Intel i7", "16 GB", "512 GB SSD"), null, null, photoSet),
                new ResponsibilityTermEquipmentItem(
                    "TM-0001", "Telemóvel", "Samsung", "Galaxy A05s", null, "MobilePhone", null,
                    new ResponsibilityTermMobilePhoneSpecification("350000000000123", "910000555", "89351000000000000123"), null, photoSet),
            ],
        };
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, ConfiguredIssuer);

        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
    }

    [Theory]
    [InlineData("Dell")]
    [InlineData("Latitude 5450")]
    [InlineData("A-B/C")]
    [InlineData("SN-2026-000123")]
    public void WithBreakOpportunities_LeavesValuesWithNoRunOfTenUnchanged(string input)
    {
        // Every hyphen/space/slash resets the run counter, so as long as no
        // segment between them reaches 10 characters, nothing is inserted.
        Assert.Equal(input, ResponsibilityTermPdfGenerator.WithBreakOpportunities(input));
    }

    [Fact]
    public void WithBreakOpportunities_InsertsAnInvisibleBreakAfterTenCharacters_ButNeverAsTheTrailingCharacter()
    {
        // "OriginalManufacturer" is exactly 20 characters (the real value
        // that originally triggered the column-overflow defect this helper
        // exists to fix), so a naive "every 10 characters" rule would insert
        // a break after the 10th AND the 20th (i.e. last) character. The
        // second one must NOT be inserted: a break with nothing after it can
        // never actually wrap anything, and (per a real regression this
        // exact codebase hit) a trailing separator in that position is
        // exactly the dangling-punctuation defect the fix must prevent.
        var result = ResponsibilityTermPdfGenerator.WithBreakOpportunities("OriginalManufacturer");

        Assert.Equal($"OriginalMa{InvisibleBreak}nufacturer", result);
        Assert.False(result.EndsWith(InvisibleBreak), "A break opportunity must never be the last character of the value.");
        // Stripping the inserted break character must reproduce the original text exactly.
        Assert.Equal("OriginalManufacturer", result.Replace(InvisibleBreak.ToString(), string.Empty));
    }

    [Fact]
    public void WithBreakOpportunities_InsertsABreakInsideALongDigitRunEvenWhenHyphensSurroundIt()
    {
        // A hyphen resets the run counter, but the digits *between* hyphens
        // can themselves be long enough to need a break — e.g. a serial
        // number like "SN-ORIGINAL-000000000000001" has a 15-digit unbroken
        // tail. This is intentional: any unbroken run of 10+ characters is a
        // potential column overflow, regardless of what precedes it.
        var result = ResponsibilityTermPdfGenerator.WithBreakOpportunities("SN-ORIGINAL-000000000000001");

        Assert.Contains(InvisibleBreak, result);
        Assert.DoesNotContain('-', result.Replace("SN-ORIGINAL-", string.Empty));
        Assert.Equal("SN-ORIGINAL-000000000000001", result.Replace(InvisibleBreak.ToString(), string.Empty));
    }

    [Fact]
    public void WithBreakOpportunities_NeverInsertsAsTheLastCharacter_EvenWhenTheRunEndsExactlyOnAChunkBoundary()
    {
        // A run whose length is itself an exact multiple of 10 (e.g. a
        // 20-digit SIM serial) would, under a naive "insert every 10
        // characters" rule, get a break appended immediately after the very
        // last digit — which is the exact real-world defect reported against
        // this generator ("8935106000-1184590137-", with a dangling trailing
        // hyphen after the true value ends).
        var result = ResponsibilityTermPdfGenerator.WithBreakOpportunities("89351000000000000123");

        Assert.False(result.EndsWith(InvisibleBreak));
        Assert.Equal("89351000000000000123", result.Replace(InvisibleBreak.ToString(), string.Empty));
    }

    [Fact]
    public void WithBreakOpportunities_NeverInsertsAVisibleCharacter()
    {
        // The defining requirement of the fix: whatever character is used for
        // wrapping, it must never be one a reader could mistake for part of
        // the identifier. U+200B has no visible glyph in any font.
        var result = ResponsibilityTermPdfGenerator.WithBreakOpportunities("89351000000000000123");

        Assert.DoesNotContain('-', result);
        Assert.DoesNotContain((char)0x00AD, result); // the old, rejected soft-hyphen approach
    }

    [Fact]
    public void Generate_ConsultantNameLandingOnATenCharacterWordBoundary_RendersWithNoTrailingHyphen()
    {
        // The exact real-world regression: "Walkthrough Consultant" ends in a
        // 10-letter word ("Consultant"), which used to make the old
        // soft-hyphen approach append a dangling, always-visible "-" right
        // after the name in every table that shows it (UTILIZADOR DO
        // PORTÁTIL/TELEMÓVEL) — even though the name fits the column with no
        // wrapping needed at all. The name is human-readable text, so it must
        // now go through the render pipeline completely unmodified.
        var model = BuildModel(itemCount: 1, includeLaptop: true, includeProfessionalContext: true) with
        {
            Consultant = new ResponsibilityTermConsultantInfo(
                "Walkthrough Consultant", "234567897", "Rua de Teste 123, Lisboa", "+351910000000", "walkthrough.consultant@example.invalid"),
        };
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, ConfiguredIssuer);
        var shownText = ExtractShownText(bytes);

        // Deliberately not Assert.Contains/DoesNotContain here: their string
        // overloads compare with the current culture, and standard Unicode
        // collation treats a zero-width space as an ignorable/zero-weight
        // character — so a *culture-aware* search for "Consultant" + ZWSP
        // spuriously matches plain "Consultant" followed by anything, even
        // when no such character is actually present. Ordinal Contains is
        // the only way to assert on the literal codepoints the renderer
        // produced.
        Assert.True(shownText.Contains("WalkthroughConsultant", StringComparison.Ordinal));
        Assert.False(shownText.Contains("WalkthroughConsultant-", StringComparison.Ordinal));
        Assert.False(shownText.Contains("Consultant" + InvisibleBreak, StringComparison.Ordinal));
        // The source value itself must never be touched by rendering.
        Assert.Equal("Walkthrough Consultant", model.Consultant.FullName);
    }

    [Fact]
    public void Generate_MobilePhoneWithLongImeiAndSimSerial_RendersBothWithNoInsertedVisibleCharacter()
    {
        // The other half of the real-world regression: a 20-digit SIM serial
        // ("89351000000000000123") used to render as "8935106000-1184590137-"
        // — two inserted, always-visible hyphens that do not appear anywhere
        // in the actual stored value and that made the two halves no longer
        // concatenate back to it. IMEI is the same kind of machine
        // identifier and must be held to the same standard.
        const string imei = "356789123456789";
        const string simSerial = "89351000000000000123";
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: true) with
        {
            EquipmentItems =
            [
                new ResponsibilityTermEquipmentItem(
                    AssetTag: "TM-0001", EquipmentTypeName: "Telemóvel", Manufacturer: "Samsung", Model: "Galaxy A05s",
                    SerialNumber: null, Kind: "MobilePhone", LaptopSpecification: null,
                    MobilePhoneSpecification: new ResponsibilityTermMobilePhoneSpecification(imei, "961234567", simSerial),
                    Notes: null, PhotoSet: null),
            ],
        };
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, ConfiguredIssuer);
        var shownText = ExtractShownText(bytes);

        // The exact original digit sequences must appear intact and
        // contiguous — proof that no visible (or otherwise byte-emitting)
        // character was spliced into them anywhere. Ordinal, for the same
        // reason as the consultant-name test above.
        Assert.True(shownText.Contains(imei, StringComparison.Ordinal));
        Assert.True(shownText.Contains(simSerial, StringComparison.Ordinal));
        Assert.False(shownText.Contains(imei + "-", StringComparison.Ordinal));
        Assert.False(shownText.Contains(simSerial + "-", StringComparison.Ordinal));
        // Stored specification values are never mutated by rendering.
        var mobileSpec = model.EquipmentItems[0].MobilePhoneSpecification;
        Assert.Equal(imei, mobileSpec!.Imei);
        Assert.Equal(simSerial, mobileSpec.SimSerialNumber);
    }

    [Fact]
    public void Generate_EquipmentTableGenericItem_LeavesAGenuineHyphenInASerialNumberUntouched()
    {
        // A real, stored hyphen (as opposed to an inserted break point) is
        // ordinary punctuation within the identifier and must render exactly
        // as-is — WithBreakOpportunities already resets its run counter on
        // every real hyphen, so this is regression coverage for that staying
        // true after the U+00AD -> U+200B change.
        const string serialNumber = "WALKTHROUGH-SN-001";
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false) with
        {
            EquipmentItems =
            [
                new ResponsibilityTermEquipmentItem(
                    AssetTag: "MN-0001", EquipmentTypeName: "Monitor", Manufacturer: "LG", Model: "27UL",
                    SerialNumber: serialNumber, Kind: "Generic", LaptopSpecification: null,
                    MobilePhoneSpecification: null, Notes: null, PhotoSet: null),
            ],
        };
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);

        var bytes = generator.Generate(model, ConfiguredIssuer);
        var shownText = ExtractShownText(bytes);

        Assert.True(shownText.Contains(serialNumber, StringComparison.Ordinal));
    }

    // --- Final signing block (every composition) ---------------------------

    /// <summary>
    /// The signing block's text as <see cref="ExtractShownText"/> sees it: the
    /// renderer positions words rather than drawing spaces, and escapes
    /// parentheses inside PDF string literals.
    /// </summary>
    private const string ShownSignatureLabel = @"Assinaturado\(a\)Consultor\(a\):";

    private const string ShownDateLabel = "Datadereceçãodosequipamentos:";

    private const string ShownBlankDate = "____/____/______";

    private const string ShownV1DateLabel = "Data:";

    private const string ShownSignatureLine = "____________________________________________________";

    private static readonly ResponsibilityTermPhotoSet ThreePhotos = new(
        new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc), [MinimalValidPngBytes, MinimalValidPngBytes, MinimalValidPngBytes]);

    private static ResponsibilityTermEquipmentItem GenericItem(string assetTag, string typeName, string manufacturer, string model) =>
        new(assetTag, typeName, manufacturer, model, $"SN-{assetTag}", "Generic", null, null, null, null);

    private static ResponsibilityTermEquipmentItem LaptopItem(string assetTag = "LT-0001") =>
        new(assetTag, "Portátil", "Dell", "Latitude 5450", $"SN-{assetTag}", "Laptop",
            new ResponsibilityTermLaptopSpecification("Intel i7", "16 GB", "512 GB SSD"), null, null, ThreePhotos);

    private static ResponsibilityTermEquipmentItem MobilePhoneItem(string assetTag = "TM-0001") =>
        new(assetTag, "Telemóvel", "Samsung", "Galaxy A05s", null, "MobilePhone", null,
            new ResponsibilityTermMobilePhoneSpecification("350000000000123", "910000555", "89351000000000000123"), null, ThreePhotos);

    public static TheoryData<string, ResponsibilityTermEquipmentItem[]> EveryComposition => new()
    {
        { "Keyboard only", [GenericItem("KB-0001", "Teclado", "Logitech", "K120")] },
        { "Monitor only", [GenericItem("MN-0001", "Monitor", "LG", "27UL")] },
        { "Mouse only", [GenericItem("RT-0001", "Rato", "Logitech", "M100")] },
        { "Headset only", [GenericItem("HS-0001", "Headset", "Jabra", "Evolve2 30")] },
        { "Laptop only", [LaptopItem()] },
        { "MobilePhone only", [MobilePhoneItem()] },
        { "Laptop + Monitor + Keyboard", [LaptopItem(), GenericItem("MN-0001", "Monitor", "LG", "27UL"), GenericItem("KB-0001", "Teclado", "Logitech", "K120")] },
        { "Laptop + MobilePhone + Mouse", [LaptopItem(), MobilePhoneItem(), GenericItem("RT-0001", "Rato", "Logitech", "M100")] },
    };

    [Theory]
    [MemberData(nameof(EveryComposition))]
    public void Generate_EveryEquipmentComposition_EndsWithExactlyOneSigningBlockOnTheLastPage(
        string composition, ResponsibilityTermEquipmentItem[] items)
    {
        // Regression: Template V2 only drew the device forms' own
        // Entregue/Recebido lines, so a generic-only Term (a single keyboard)
        // had nowhere for the consultant to sign or date at all.
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: true) with { EquipmentItems = items };

        var pages = ExtractShownTextPerStream(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));

        AssertSingleSigningBlockOnLastPage(pages, composition);
    }

    [Fact]
    public void Generate_LaptopOnly_KeepsTheLaptopFormAlongsideTheSigningBlock()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false) with { EquipmentItems = [LaptopItem()] };

        var text = ExtractShownText(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));

        Assert.True(text.Contains("FICHADEEMPRÉSTIMODEPORTÁTIL", StringComparison.Ordinal));
        Assert.True(text.Contains("1.200,00", StringComparison.Ordinal));
        Assert.True(text.Contains("REGISTOFOTOGRÁFICODOEQUIPAMENTO", StringComparison.Ordinal));
        Assert.Equal(1, CountOccurrences(text, ShownSignatureLabel));
        // The generic-only declaration is still not added to a laptop-only Term.
        Assert.False(text.Contains("DeclaraçãodeResponsabilidade", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_MobilePhoneOnly_KeepsTheMobilePhoneFormAlongsideTheSigningBlock()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false) with { EquipmentItems = [MobilePhoneItem()] };

        var text = ExtractShownText(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));

        Assert.True(text.Contains("FICHADEEMPRÉSTIMODETELEMÓVEL", StringComparison.Ordinal));
        Assert.True(text.Contains("180,00", StringComparison.Ordinal));
        Assert.True(text.Contains("ENTREGADOTELEMÓVEL", StringComparison.Ordinal));
        // The old return sub-form (its own Devolvido/Recebido pair) is gone too.
        Assert.False(text.Contains("DEVOLUÇÃODOTELEMÓVEL", StringComparison.Ordinal));
        Assert.Equal(1, CountOccurrences(text, ShownSignatureLabel));
        Assert.False(text.Contains("DeclaraçãodeResponsabilidade", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_GenericOnly_KeepsTheDeclarationBeforeTheSigningBlock()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false) with
        {
            EquipmentItems = [GenericItem("KB-0001", "Teclado", "Logitech", "K120")],
        };

        var text = ExtractShownText(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));

        var declaration = text.IndexOf("DeclaraçãodeResponsabilidade", StringComparison.Ordinal);
        var signature = text.IndexOf(ShownSignatureLabel, StringComparison.Ordinal);
        var metadata = text.IndexOf("Documentogeradoem", StringComparison.Ordinal);
        Assert.True(declaration >= 0 && declaration < signature && signature < metadata,
            $"Expected declaration < signature < generated-document note, got {declaration}, {signature}, {metadata}.");
        Assert.False(text.Contains("FICHADEEMPRÉSTIMO", StringComparison.Ordinal));
        Assert.False(text.Contains("1.200,00", StringComparison.Ordinal));
        Assert.False(text.Contains("180,00", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_MultiPageTerms_PlaceTheSigningBlockWholeOnTheLastPage_WithEveryFooterIntact()
    {
        // Sweeps the item count across the first page breaks, so the block
        // lands near the bottom of a page in several variants — it must never
        // be split from its label or left behind on an earlier page.
        var generator = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic);
        var sawMultiPage = false;

        for (var itemCount = 1; itemCount <= 40; itemCount++)
        {
            var model = BuildModel(itemCount, includeLaptop: false, includeProfessionalContext: true);
            var pages = ExtractShownTextPerStream(generator.Generate(model, ConfiguredIssuer));

            sawMultiPage |= pages.Count > 1;
            AssertSingleSigningBlockOnLastPage(pages, $"{itemCount} generic items");
        }

        var mixed = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: true) with
        {
            EquipmentItems = [LaptopItem("LT-0001"), LaptopItem("LT-0002"), MobilePhoneItem(), GenericItem("MN-0001", "Monitor", "LG", "27UL")],
        };
        var mixedPages = ExtractShownTextPerStream(generator.Generate(mixed, ConfiguredIssuer));
        Assert.True(mixedPages.Count > 1);
        AssertSingleSigningBlockOnLastPage(mixedPages, "two laptops + phone + monitor");

        Assert.True(sawMultiPage, "The sweep never produced a multi-page Term.");
    }

    public static TheoryData<string, ResponsibilityTermEquipmentItem[]> DeviceCompositions => new()
    {
        { "Laptop only", [LaptopItem()] },
        { "MobilePhone only", [MobilePhoneItem()] },
        { "Laptop + MobilePhone", [LaptopItem(), MobilePhoneItem()] },
        { "Two laptops + two phones", [LaptopItem("LT-0001"), LaptopItem("LT-0002"), MobilePhoneItem("TM-0001"), MobilePhoneItem("TM-0002")] },
    };

    [Theory]
    [MemberData(nameof(DeviceCompositions))]
    public void Generate_DeviceForms_HaveNoSignatureFieldsOfTheirOwn_OnlyTheOneFinalBlock(
        string composition, ResponsibilityTermEquipmentItem[] items)
    {
        // The device forms used to carry an "Entregue / Recebido — Assinatura
        // e Data" pair per item (and the phone form a "Devolvido / Recebido"
        // return pair). The consultant now signs once, at the end.
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: true) with { EquipmentItems = items };

        var pages = ExtractShownTextPerStream(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));
        var text = string.Concat(pages);

        AssertSingleSigningBlockOnLastPage(pages, composition);
        foreach (var label in ObsoleteSignatureLabels)
        {
            Assert.Equal(0, CountOccurrences(text, label));
        }

        // The delivery sentences (past tense, ordinary prose) and every device
        // form's legal clauses are kept, once per device.
        var laptops = items.Count(i => i.Kind == "Laptop");
        var phones = items.Count(i => i.Kind == "MobilePhone");
        Assert.Equal(laptops, CountOccurrences(text, "Oportátilfoientregueemperfeitoestadodeconservação."));
        Assert.Equal(phones, CountOccurrences(text, "Otelemóvelfoientregueemperfeitoestadodeconservação."));
        Assert.Equal(laptops, CountOccurrences(text, "1.200,00"));
        Assert.Equal(phones, CountOccurrences(text, "180,00"));
        Assert.Equal(laptops + phones, CountOccurrences(text, TestClientProfiles.SupportContact));
        Assert.Equal(laptops + phones, CountOccurrences(text, "REGISTOFOTOGRÁFICODOEQUIPAMENTO"));

        // The photo heading is never left alone at a page bottom: wherever it
        // is drawn, its photos' caption is on the same page.
        Assert.All(pages, page => Assert.Equal(
            CountOccurrences(page, "REGISTOFOTOGRÁFICODOEQUIPAMENTO"),
            CountOccurrences(page, "Fotografiasdereferênciadoestadodoequipamento.")));
    }

    [Fact]
    public void Generate_TemplateVersion1_StillHasItsOwnSingleSigningBlock()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: true, includeProfessionalContext: true) with { TemplateVersion = 1 };

        var text = ExtractShownText(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));

        Assert.Equal(1, CountOccurrences(text, ShownSignatureLabel));
        Assert.Equal(1, CountOccurrences(text, ShownV1DateLabel));
    }

    // --- Reception date (a carrier term may be prepared before receipt) ----

    [Theory]
    [MemberData(nameof(EveryComposition))]
    public void Generate_TermPreparedBeforeReceipt_HasOnlyTheBlankReceptionDateField(
        string composition, ResponsibilityTermEquipmentItem[] items)
    {
        // A carrier term generated after "Registar envio": DeliveredOn does
        // not exist yet, so the model carries no delivery date at all.
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: true) with
        {
            DeliveryDate = null,
            EquipmentItems = items,
        };

        var pages = ExtractShownTextPerStream(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));

        AssertSingleSigningBlockOnLastPage(pages, composition);
        AssertStatesNoDeliveryDate(string.Concat(pages), composition);
    }

    [Fact]
    public void Generate_TermAfterConfirmedDelivery_NeverPrintsTheRecordedDeliveryDate_AndKeepsTheFieldBlank()
    {
        // One template, one meaning: even when DeliveredOn is known, the
        // consultant completes the reception date when signing — the field is
        // never prefilled, and the date is not repeated elsewhere.
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: true) with
        {
            DeliveryDate = new DateOnly(2026, 10, 5),
            EquipmentItems = [LaptopItem(), MobilePhoneItem(), GenericItem("MN-0001", "Monitor", "LG", "27UL")],
        };

        var pages = ExtractShownTextPerStream(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));
        var text = string.Concat(pages);

        Assert.False(text.Contains("05/10/2026", StringComparison.Ordinal));
        AssertSingleSigningBlockOnLastPage(pages, "post-delivery mixed term");
        AssertStatesNoDeliveryDate(text, "post-delivery mixed term");
    }

    [Fact]
    public void Generate_TermPreparedBeforeReceipt_KeepsTheDeviceFormsAndTheirSubstantiveContent()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: true) with
        {
            DeliveryDate = null,
            EquipmentItems = [LaptopItem(), MobilePhoneItem()],
        };

        var text = ExtractShownText(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));

        Assert.True(text.Contains("UTILIZADORDOPORTÁTIL", StringComparison.Ordinal));
        Assert.True(text.Contains("UTILIZADORDOTELEMÓVEL", StringComparison.Ordinal));
        Assert.True(text.Contains("DuraçãodoEmpréstimo", StringComparison.Ordinal));
        Assert.True(text.Contains("JoãoExemplo", StringComparison.Ordinal));
        Assert.True(text.Contains("1.200,00", StringComparison.Ordinal));
        Assert.True(text.Contains("180,00", StringComparison.Ordinal));
        Assert.True(text.Contains(TestClientProfiles.SupportContact, StringComparison.Ordinal));
        Assert.True(text.Contains("REGISTOFOTOGRÁFICODOEQUIPAMENTO", StringComparison.Ordinal));
        Assert.True(text.Contains("Responsávelpelaentrega:AnaDemo", StringComparison.Ordinal));
    }

    /// <summary>
    /// No delivery-date label anywhere besides the single blank consultant
    /// field, and no date-shaped value except the two that are not delivery
    /// dates: the photo capture date and the generation timestamp.
    /// </summary>
    private static void AssertStatesNoDeliveryDate(string text, string scenario)
    {
        foreach (var label in new[] { "Datadeentrega", "DatadeEntrega", "DatadeEmpréstimo", "Entreguepor", "EquipamentosEntregues" })
        {
            Assert.False(text.Contains(label, StringComparison.Ordinal), $"{scenario}: unexpected '{label}'.");
        }

        var allowedDates = new[] { "10/09/2026", "12/09/2026" }; // photo set capture; "Documento gerado em"
        var dates = Regex.Matches(text, @"\d{2}/\d{2}/\d{4}").Select(m => m.Value).Distinct().ToList();
        Assert.Contains("12/09/2026", dates); // the scan itself sees dates
        Assert.All(dates, date => Assert.Contains(date, allowedDates));
    }

    /// <summary>
    /// The device forms' old per-item signature pairs ("Entregue: ____" /
    /// "Recebido: ____" / "Devolvido: ____", each captioned "Assinatura e
    /// Data") are gone. Matches the colon-terminated field labels and the
    /// caption only, so ordinary prose ("O portátil foi entregue em…") is not
    /// affected.
    /// </summary>
    private static void AssertNoObsoleteSignatureFields(string text, string scenario)
    {
        foreach (var label in ObsoleteSignatureLabels)
        {
            Assert.False(text.Contains(label, StringComparison.Ordinal), $"{scenario}: obsolete signature field '{label}' present.");
        }
    }

    private static readonly string[] ObsoleteSignatureLabels = ["Entregue:", "Recebido:", "Devolvido:", "AssinaturaeData"];

    // --- Client profile content (docs/WHITE_LABEL.md) ------------------------

    [Fact]
    public void Generate_DeviceForms_RenderTheProfileClausesWithTheirIndemnityFilledIn()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false) with
        {
            EquipmentItems = [LaptopItem(), MobilePhoneItem()],
        };

        var text = ExtractShownText(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));

        Assert.Equal(1, CountOccurrences(text, "Oportátilécedidopelaorganizaçãosintética."));
        Assert.Equal(1, CountOccurrences(text, "Otelemóvelécedidopelaorganizaçãosintética."));
        // The extractor decodes as Latin-1, so the euro sign is not asserted.
        Assert.Equal(1, CountOccurrences(text, "1.200,00"));
        Assert.Equal(1, CountOccurrences(text, @"\(mileduzentoseuros\)"));
        Assert.Equal(1, CountOccurrences(text, "180,00"));
        Assert.Equal(1, CountOccurrences(text, @"\(centoeoitentaeuros\)"));
        Assert.Equal(0, CountOccurrences(text, "{indemnity}"));
    }

    [Fact]
    public void Generate_MobilePhoneForm_NamesTheProfileOperator_OrUsesTheGenericLabelWithoutOne()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false) with { EquipmentItems = [MobilePhoneItem()] };
        var withoutOperator = TestClientProfiles.Synthetic with
        {
            Identity = TestClientProfiles.Synthetic.Identity with { MobileCarrierName = null },
        };

        var named = ExtractShownText(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));
        var generic = ExtractShownText(new ResponsibilityTermPdfGenerator(withoutOperator).Generate(model, ConfiguredIssuer));

        Assert.True(named.Contains("CARTÃOEXEMPLO", StringComparison.Ordinal));
        Assert.True(named.Contains("CartãoExemplo:910000555", StringComparison.Ordinal));
        Assert.True(generic.Contains("DESIGNAÇÃODOTELEMÓVEL", StringComparison.Ordinal));
        Assert.False(generic.Contains("CARTÃO", StringComparison.Ordinal));
        Assert.True(generic.Contains("N.ºdetelemóvel:910000555", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_WithoutAProfileLogo_EmbedsNoLogoImage()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false) with
        {
            EquipmentItems = [GenericItem("KB-0001", "Teclado", "Logitech", "K120")],
        };
        var withoutLogo = TestClientProfiles.Synthetic with
        {
            ResponsibilityTerm = TestClientProfiles.Synthetic.ResponsibilityTerm with { Logo = null },
        };

        var withLogoBytes = new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer);
        var withoutLogoBytes = new ResponsibilityTermPdfGenerator(withoutLogo).Generate(model, ConfiguredIssuer);

        Assert.True(CountEmbeddedImageObjects(withLogoBytes) > 0);
        Assert.Equal(0, CountEmbeddedImageObjects(withoutLogoBytes));
    }

    [Fact]
    public void Generate_PrintsTheDemonstrationNotice_OnlyWhenTheProfileHasOne()
    {
        var model = BuildModel(itemCount: 1, includeLaptop: false, includeProfessionalContext: false);
        var demonstration = TestClientProfiles.Synthetic with
        {
            ResponsibilityTerm = TestClientProfiles.Synthetic.ResponsibilityTerm with { DemonstrationNotice = "Modelo de demonstração." },
        };

        var withNotice = ExtractShownText(new ResponsibilityTermPdfGenerator(demonstration).Generate(model, ConfiguredIssuer));
        var withoutNotice = ExtractShownText(new ResponsibilityTermPdfGenerator(TestClientProfiles.Synthetic).Generate(model, ConfiguredIssuer));

        Assert.Equal(1, CountOccurrences(withNotice, "Modelodedemonstração."));
        Assert.Equal(0, CountOccurrences(withoutNotice, "Modelodedemonstração."));
    }

    /// <summary>
    /// One page per stream, each with its "Página k de N" footer; the signing
    /// label, its line, the date field and the generated-document note all on
    /// page N, each of the first three exactly once in the whole document.
    /// </summary>
    private static void AssertSingleSigningBlockOnLastPage(List<string> pages, string scenario)
    {
        var footers = pages.Select(page => Regex.Match(page, @"Página(\d+)de(\d+)")).ToList();
        Assert.All(footers, footer => Assert.True(footer.Success, $"{scenario}: a page is missing its footer."));
        var total = int.Parse(footers[0].Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(pages.Count, total);
        Assert.Equal(
            Enumerable.Range(1, total),
            footers.Select(f => int.Parse(f.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Order());

        var whole = string.Concat(pages);
        Assert.True(CountOccurrences(whole, ShownSignatureLabel) == 1, $"{scenario}: expected exactly one signature label.");
        Assert.True(CountOccurrences(whole, ShownDateLabel) == 1, $"{scenario}: expected exactly one date field.");
        Assert.True(CountOccurrences(whole, ShownSignatureLine) == 1, $"{scenario}: expected exactly one signature line.");
        Assert.True(CountOccurrences(whole, ShownBlankDate) == 1, $"{scenario}: expected exactly one blank reception date.");
        AssertNoObsoleteSignatureFields(whole, scenario);

        var lastPage = pages[footers.FindIndex(f => f.Groups[1].Value == f.Groups[2].Value)];
        var label = lastPage.IndexOf(ShownSignatureLabel, StringComparison.Ordinal);
        var line = lastPage.IndexOf(ShownSignatureLine, StringComparison.Ordinal);
        var date = lastPage.IndexOf(ShownDateLabel, StringComparison.Ordinal);
        var blank = lastPage.IndexOf(ShownBlankDate, StringComparison.Ordinal);
        var note = lastPage.IndexOf("Documentogeradoem", StringComparison.Ordinal);
        Assert.True(label >= 0 && label < line && line < date && date < blank && blank < note,
            $"{scenario}: signing block not whole and in order on the last page ({label}, {line}, {date}, {blank}, {note}).");
    }

    /// <summary>
    /// Decompresses every FlateDecode content stream in the PDF and pulls out
    /// the literal parenthesized strings passed to the "Tj" text-showing
    /// operator, concatenated in stream order. This works only because the
    /// embedded Liberation Sans font is a simple (non-CID) font whose
    /// character codes are the plain Latin-1/WinAnsi bytes of the visible
    /// text — verified empirically against this exact generator's real
    /// output before relying on it here — so this is a genuine "what did the
    /// renderer actually draw" assertion, not a proxy for it, while still
    /// needing no PDF-parsing dependency beyond the BCL. Do not reuse this
    /// technique against a document that embeds a CID-keyed/Identity-H font;
    /// it would silently extract glyph-index bytes instead of text.
    /// </summary>
    internal static string ExtractShownText(byte[] pdfBytes) => string.Concat(ExtractShownTextPerStream(pdfBytes));

    /// <summary>
    /// <see cref="ExtractShownText"/>, one entry per text-bearing content
    /// stream — PDFsharp writes one content stream per page, so each entry is
    /// one page's drawn text (its footer included).
    /// </summary>
    private static List<string> ExtractShownTextPerStream(byte[] pdfBytes)
    {
        var raw = Encoding.Latin1.GetString(pdfBytes);
        var textBearingStreams = new List<string>();

        foreach (Match streamStart in Regex.Matches(raw, @"stream\r?\n", RegexOptions.Singleline))
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
                using var compressed = new MemoryStream(streamBytes) { Position = 2 }; // skip the 2-byte zlib header
                using var inflate = new DeflateStream(compressed, CompressionMode.Decompress);
                using var decompressed = new MemoryStream();
                inflate.CopyTo(decompressed);
                var text = Encoding.Latin1.GetString(decompressed.ToArray());
                if (text.Contains("Tj"))
                {
                    textBearingStreams.Add(text);
                }
            }
            catch (InvalidDataException)
            {
                // Not a Flate-compressed content stream we can decode this way (e.g. embedded image data) - skip.
            }
        }

        return textBearingStreams
            .Select(stream => string.Concat(
                Regex.Matches(stream, @"\(((?:[^()\\]|\\.)*)\)\s*Tj").Select(literal => literal.Groups[1].Value)))
            .ToList();
    }

    internal static int CountOccurrences(string haystack, string needle)
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

    private static ResponsibilityTermDocumentModel BuildModel(
        int itemCount, bool includeLaptop, bool includeProfessionalContext, DateTime? generatedAtUtc = null)
    {
        var items = new List<ResponsibilityTermEquipmentItem>();
        for (var i = 0; i < itemCount; i++)
        {
            items.Add(new ResponsibilityTermEquipmentItem(
                AssetTag: $"LT-{i:D4}",
                EquipmentTypeName: includeLaptop ? "Laptop" : "Monitor",
                Manufacturer: "Dell",
                Model: "Latitude 5450",
                SerialNumber: $"SN{i:D6}",
                Kind: includeLaptop ? "Laptop" : "Generic",
                LaptopSpecification: includeLaptop
                    ? new ResponsibilityTermLaptopSpecification("Intel i7-1365U", "16 GB", "512 GB SSD")
                    : null,
                MobilePhoneSpecification: null,
                Notes: null,
                PhotoSet: null));
        }

        return new ResponsibilityTermDocumentModel(
            DocumentNumber: "TR-2026-000001",
            Version: 1,
            TemplateVersion: ResponsibilityTermTemplate.CurrentVersion,
            DeliveryDate: new DateOnly(2026, 9, 10),
            DeliveredByDisplayName: "Ana Demo",
            GeneratedAtUtc: generatedAtUtc ?? new DateTime(2026, 9, 12, 14, 22, 0, DateTimeKind.Utc),
            GeneratedByDisplayName: "Nina Example",
            Consultant: new ResponsibilityTermConsultantInfo("João Exemplo", "123456789", "Rua Teste 1, Lisboa", "+351900000000", "joao@example.invalid"),
            ProfessionalContext: includeProfessionalContext
                ? new ResponsibilityTermProfessionalContext("External", "ServiceProvider", "Backend Developer", "Acme Corp")
                : null,
            EquipmentItems: items);
    }
}
