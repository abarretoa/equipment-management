using System.Globalization;
using EquipmentManagement.Domain.Files;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.ClientProfiles;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace EquipmentManagement.Infrastructure.Documents;

/// <summary>
/// Renders the Responsibility Term as a plain, self-contained A4 PDF via
/// MigraDoc/PDFsharp (Phase 6 scope). No remote content, no
/// JavaScript, no external resources — MigraDoc/PDFsharp are pure drawing
/// libraries with no such capability at all (Phase 6 scope).
/// The layout and the product's own wording live here, versioned by
/// <see cref="ResponsibilityTermDocumentModel.TemplateVersion"/> — never
/// scattered across controllers or services (Phase 6 scope).
/// The client's own content (logo, accent colour, device-form legal clauses,
/// indemnities, mobile operator) comes from the deployment's
/// <see cref="ClientProfile"/> (docs/WHITE_LABEL.md); Template V1 uses none
/// of it.
/// </summary>
public sealed class ResponsibilityTermPdfGenerator(ClientProfile profile) : IResponsibilityTermPdfGenerator
{
    /// <summary>
    /// Defensive upper bound on generated output (Phase 6 scope) —
    /// a Responsibility Term is normally a few pages; anything beyond this
    /// indicates a rendering bug (e.g. a runaway loop), not a legitimate
    /// document, and should fail loudly rather than silently consume
    /// unbounded memory/storage.
    /// </summary>
    private const long MaxGeneratedPdfSizeBytes = 20 * 1024 * 1024;

    /// <summary>
    /// A genuinely invisible line-break opportunity, used by <see cref="WithBreakOpportunities"/>.
    /// Written as an explicit escape, never a literal character, so it can't be
    /// silently corrupted by file-encoding round-trips.
    /// <para>
    /// This is U+200B (zero-width space), not U+00AD (soft hyphen), by
    /// deliberate, empirically-verified choice: a throwaway probe against the
    /// exact installed PDFsharp-MigraDoc 6.2.4 renderer showed that this
    /// version always draws U+00AD as a literal visible hyphen glyph wherever
    /// it appears — including when nothing follows it (producing a dangling
    /// "-" after values that never needed to wrap at all, e.g. "Walkthrough
    /// Consultant-") and including at a genuine mid-value wrap (producing
    /// "8935106000-" / "1184590137-" for a plain 20-digit SIM serial, which no
    /// longer concatenates back to the original value). U+200B, rendered by
    /// the same probe, wraps a long unbroken run onto a second line with zero
    /// visible characters either way, so the original value is always exactly
    /// reconstructible from the rendered text. Do not change this back to
    /// U+00AD without re-verifying against the actual renderer.
    /// </para>
    /// </summary>
    private const char InvisibleBreak = (char)0x200B;

    private const string ResponsibilityDeclarationTextV1 =
        "O(a) Consultor(a) acima identificado(a) declara ter recebido o(s) equipamento(s) " +
        "elencado(s) neste Termo de Responsabilidade, em conformidade com o estado registado " +
        "na respetiva inspeção de entrega, comprometendo-se a zelar pela sua correta utilização " +
        "e conservação, a utilizá-lo(s) exclusivamente no âmbito da sua atividade profissional, " +
        "e a devolvê-lo(s) à empresa quando solicitado ou no termo da relação profissional, nos " +
        "termos e procedimentos internos aplicáveis.";

    static ResponsibilityTermPdfGenerator() => LiberationFontResolver.Register();

    public byte[] Generate(ResponsibilityTermDocumentModel model, DocumentIssuerOptions issuer)
    {
        // MigraDoc's AddImage(...) only accepts a file path, not a byte
        // stream (confirmed against the installed PDFsharp-MigraDoc package
        // — there is no ImageSource/stream API in this version). Template V2
        // therefore writes each embedded image (the client logo, the 3 current
        // condition photos) to a private temp file just for the duration of
        // this call, and every one of those paths is deleted here once
        // rendering has produced the final bytes — nothing is ever left
        // behind, success or failure.
        var tempFiles = new List<string>();
        try
        {
            var document = model.TemplateVersion switch
            {
                1 => BuildTemplateV1(model, issuer),
                ResponsibilityTermTemplate.CurrentVersion => BuildTemplateV2(model, issuer, profile, tempFiles),
                _ => throw new NotSupportedException($"No rendering is available for Responsibility Term template version {model.TemplateVersion}."),
            };

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();

            using var buffer = new MemoryStream();
            renderer.PdfDocument.Save(buffer, closeStream: false);
            var bytes = buffer.ToArray();

            if (bytes.LongLength > MaxGeneratedPdfSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Generated Responsibility Term PDF ({bytes.LongLength} bytes) exceeds the defensive maximum of {MaxGeneratedPdfSizeBytes} bytes.");
            }

            return bytes;
        }
        finally
        {
            foreach (var path in tempFiles)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Best-effort cleanup only — a leftover temp file is a
                    // minor housekeeping issue, never a reason to mask the
                    // real generation result/exception.
                }
            }
        }
    }

    private static Document BuildTemplateV1(ResponsibilityTermDocumentModel model, DocumentIssuerOptions issuer)
    {
        var document = new Document();
        document.Info.Title = $"Termo de Responsabilidade {model.DocumentNumber}";
        document.Info.Author = issuer.LegalName ?? string.Empty;
        document.Info.Subject = "Termo de Responsabilidade de Equipamento";

        DefineStyles(document, accentColor: null);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);

        AddFooter(section, model);
        AddTitleAndIssuer(section, model, issuer);
        AddDeliveryInfo(section, model);
        AddConsultantSection(section, model.Consultant);

        if (model.ProfessionalContext is { } context)
        {
            AddProfessionalContextSection(section, context);
        }

        AddEquipmentTable(section, model.EquipmentItems);
        AddDeclaration(section);
        AddSignatureBlock(section);
        AddMetadataFooterNote(section, model);

        return document;
    }

    /// <summary>
    /// The client's identity (logo, accent-coloured headings) plus the
    /// device-specific Laptop/Mobile Phone structures ("Ficha de Empréstimo de
    /// Portátil"/"Ficha de Empréstimo de Telemóvel"), whose legal clauses come
    /// from the client profile. Generic-kind items keep exactly V1's shared table +
    /// declaration (rendered only when at least one exists) — this template
    /// version is additive, not a redesign of the generic path. Each Laptop/
    /// MobilePhone item gets its own full device section, including the
    /// REGISTO FOTOGRÁFICO DO EQUIPAMENTO photo block from
    /// <see cref="ResponsibilityTermEquipmentItem.PhotoSet"/> — the exact 3
    /// photos frozen for this Term at generation time (see
    /// <c>ResponsibilityTermService.GenerateAsync</c>), never re-read later.
    /// </summary>
    private static Document BuildTemplateV2(
        ResponsibilityTermDocumentModel model, DocumentIssuerOptions issuer, ClientProfile profile, List<string> tempFiles)
    {
        var content = profile.ResponsibilityTerm;
        var document = new Document();
        document.Info.Title = $"Termo de Responsabilidade {model.DocumentNumber}";
        document.Info.Author = issuer.LegalName ?? string.Empty;
        document.Info.Subject = "Termo de Responsabilidade de Equipamento";

        DefineStyles(document, content.AccentColor);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);

        AddFooter(section, model);
        AddLogoAndTitle(section, model, issuer, content, tempFiles);
        AddDeliveryInfoV2(section, model);
        AddConsultantSection(section, model.Consultant);

        if (model.ProfessionalContext is { } context)
        {
            AddProfessionalContextSection(section, context);
        }

        var genericItems = model.EquipmentItems.Where(i => i.Kind is not (nameof(Kind.Laptop)) and not nameof(Kind.MobilePhone)).ToList();
        if (genericItems.Count > 0)
        {
            AddEquipmentTable(section, genericItems, "Equipamentos");
            AddDeclaration(section);
        }

        foreach (var item in model.EquipmentItems.Where(i => i.Kind == nameof(Kind.Laptop)))
        {
            AddLaptopSection(section, model, item, content.Laptop, tempFiles);
        }

        foreach (var item in model.EquipmentItems.Where(i => i.Kind == nameof(Kind.MobilePhone)))
        {
            AddMobilePhoneSection(section, model, item, profile, tempFiles);
        }

        // Unconditional: every Term, whatever its equipment composition, ends
        // with exactly one place for the consultant to sign and date it — the
        // only signature area in the document. The device sections carry no
        // signature lines of their own (no per-item "Entregue/Recebido" pair).
        AddFinalSigningBlock(section);
        AddMetadataFooterNote(section, model);

        return document;
    }

    /// <summary>Mirrors <c>EquipmentManagement.Domain.Equipments.EquipmentTypeKind</c>'s member names as plain strings — the PDF generator is deliberately database/domain-project-free (see the interface's remarks), so it matches by the snapshotted string value rather than referencing that enum type directly.</summary>
    private enum Kind
    {
        Generic,
        Laptop,
        MobilePhone,
    }

    private static void AddLogoAndTitle(
        Section section, ResponsibilityTermDocumentModel model, DocumentIssuerOptions issuer, ResponsibilityTermContent content, List<string> tempFiles)
    {
        if (content.Logo is { } logoContent)
        {
            var logoParagraph = section.AddParagraph();
            logoParagraph.Format.Alignment = ParagraphAlignment.Right;
            logoParagraph.Format.SpaceAfter = Unit.FromPoint(4);
            var extension = ImageSignatureDetector.Detect(logoContent.Bytes) == ImageContentType.Jpeg ? "jpg" : "png";
            var logoPath = WriteTempImageFile(logoContent.Bytes, extension, tempFiles);
            var logo = logoParagraph.AddImage(logoPath);
            logo.LockAspectRatio = true;
            logo.Width = Unit.FromCentimeter(logoContent.WidthCm);
        }

        if (issuer.IsConfigured)
        {
            var issuerParagraph = section.AddParagraph();
            issuerParagraph.Format.Font.Size = 9;
            issuerParagraph.Format.SpaceAfter = Unit.FromPoint(10);
            issuerParagraph.AddText(issuer.LegalName!);
            issuerParagraph.AddLineBreak();
            issuerParagraph.AddText(issuer.Address!);
            issuerParagraph.AddLineBreak();
            issuerParagraph.AddText($"NIF/NIPC: {issuer.TaxId}");
        }

        section.AddParagraph("TERMO DE RESPONSABILIDADE", "TermHeadingAccent");

        var identification = section.AddParagraph();
        identification.Format.Alignment = ParagraphAlignment.Center;
        identification.Format.SpaceAfter = Unit.FromPoint(14);
        identification.AddText($"Documento nº {model.DocumentNumber} · Versão {model.Version}");

        if (content.DemonstrationNotice is { } notice)
        {
            var noticeParagraph = section.AddParagraph();
            noticeParagraph.Format.Alignment = ParagraphAlignment.Center;
            noticeParagraph.Format.Font.Size = 8;
            noticeParagraph.Format.Font.Italic = true;
            noticeParagraph.Format.Font.Color = Colors.Gray;
            noticeParagraph.Format.SpaceAfter = Unit.FromPoint(10);
            noticeParagraph.AddText(notice);
        }
    }

    // --- Laptop ("Ficha de Empréstimo de Portátil") -------------------------

    private static void AddLaptopSection(
        Section section, ResponsibilityTermDocumentModel model, ResponsibilityTermEquipmentItem item, DeviceTermContent content, List<string> tempFiles)
    {
        section.AddParagraph("FICHA DE EMPRÉSTIMO DE PORTÁTIL", "SectionHeadingAccent");

        AddTwoColumnTable(section, "DESIGNAÇÃO DO PORTÁTIL", "Marca e Modelo", "Outras Informações (estado equipamento – novo/usado)",
            JoinNonBlank(item.Manufacturer, item.Model), item.Notes ?? "N/A");

        // No date column: the reception date is completed once, by the
        // consultant, in the final signing block (see AddFinalSigningBlock).
        AddSingleColumnTable(section, "UTILIZADOR DO PORTÁTIL", "Nome", model.Consultant.FullName);

        section.AddParagraph("EMPRÉSTIMO PORTÁTIL", "SectionHeading");
        var deliverySentence = section.AddParagraph();
        deliverySentence.Format.SpaceAfter = Unit.FromPoint(8);
        deliverySentence.AddText("O portátil foi entregue em perfeito estado de conservação.");

        // No signature lines here: the consultant signs once, in the final
        // signing block (see AddFinalSigningBlock).
        AddLegalClauses(section, content);
        AddPhotoSection(section, item, tempFiles);
    }

    // --- Mobile Phone ("Ficha de Empréstimo de Telemóvel") ------------------

    private static void AddMobilePhoneSection(
        Section section, ResponsibilityTermDocumentModel model, ResponsibilityTermEquipmentItem item, ClientProfile profile, List<string> tempFiles)
    {
        section.AddParagraph("FICHA DE EMPRÉSTIMO DE TELEMÓVEL", "SectionHeadingAccent");

        var mobile = item.MobilePhoneSpecification;
        section.AddParagraph(ResponsibilityTermText.MobilePhoneDesignationHeading(profile.Identity), "SectionHeading");
        var designationTable = section.AddTable();
        designationTable.Borders.Width = 0.5;
        designationTable.Borders.Color = Colors.Gray;
        designationTable.AddColumn(Unit.FromCentimeter(5.6));
        designationTable.AddColumn(Unit.FromCentimeter(5.6));
        designationTable.AddColumn(Unit.FromCentimeter(5.6));

        var headerRow = designationTable.AddRow();
        headerRow.Format.Font.Bold = true;
        headerRow.Shading.Color = Colors.LightGray;
        headerRow.Cells[0].AddParagraph("IMEI1");
        headerRow.Cells[1].AddParagraph("Marca e Modelo");
        headerRow.Cells[2].AddParagraph("Outras Informações (estado equipamento – novo/usado)");

        var dataRow = designationTable.AddRow();
        AddWrappableCellText(dataRow.Cells[0], mobile?.Imei ?? "-");
        AddPlainCellText(dataRow.Cells[1], JoinNonBlank(item.Manufacturer, item.Model));
        AddPlainCellText(dataRow.Cells[2], item.Notes ?? "N/A");

        var simRow = designationTable.AddRow();
        AddWrappableCellText(simRow.Cells[0], string.IsNullOrWhiteSpace(mobile?.MobileNumber) ? "-" : $"{ResponsibilityTermText.MobileNumberLabel(profile.Identity)}: {mobile!.MobileNumber}");
        AddWrappableCellText(simRow.Cells[1], string.IsNullOrWhiteSpace(mobile?.SimSerialNumber) ? "-" : $"número SIM: {mobile!.SimSerialNumber}");
        // Pin/Puk are never stored in plaintext by this application, because
        // they are secrets — the source form's cells are preserved visually, left blank
        // for manual completion, rather than populated from application data.
        AddPlainCellText(simRow.Cells[2], "Pin: ____________   Puk: ____________");

        // No date column — see the Laptop section above.
        AddTwoColumnTable(section, "UTILIZADOR DO TELEMÓVEL", "Nome", "Duração do Empréstimo", model.Consultant.FullName, "N/A");

        section.AddParagraph("ENTREGA DO TELEMÓVEL", "SectionHeading");
        var deliverySentence = section.AddParagraph();
        deliverySentence.Format.SpaceAfter = Unit.FromPoint(8);
        deliverySentence.AddText("O telemóvel foi entregue em perfeito estado de conservação.");

        // No signature lines here, and no "Devolução" sub-form: the consultant
        // signs once, in the final signing block, and a return is recorded by
        // the Return workflow, not on this document.
        AddLegalClauses(section, profile.ResponsibilityTerm.MobilePhone);
        AddPhotoSection(section, item, tempFiles);
    }

    // --- Shared V2 helpers ---------------------------------------------------

    /// <summary>Human-readable text only (a name) — rendered exactly as given, via <see cref="AddPlainCellText"/>.</summary>
    private static void AddSingleColumnTable(Section section, string heading, string header, string value)
    {
        section.AddParagraph(heading, "SectionHeading");
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.Gray;
        table.AddColumn(Unit.FromCentimeter(17));

        var headerRow = table.AddRow();
        headerRow.Format.Font.Bold = true;
        headerRow.Shading.Color = Colors.LightGray;
        headerRow.Cells[0].AddParagraph(header);

        var dataRow = table.AddRow();
        AddPlainCellText(dataRow.Cells[0], value);
    }

    /// <summary>Both columns are always human-readable text (a name, a manufacturer/model pair, a formatted date, notes) — never a machine identifier — so cell values render exactly as given, via <see cref="AddPlainCellText"/>.</summary>
    private static void AddTwoColumnTable(Section section, string heading, string header1, string header2, string value1, string value2)
    {
        section.AddParagraph(heading, "SectionHeading");
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.Gray;
        table.AddColumn(Unit.FromCentimeter(8.5));
        table.AddColumn(Unit.FromCentimeter(8.5));

        var headerRow = table.AddRow();
        headerRow.Format.Font.Bold = true;
        headerRow.Shading.Color = Colors.LightGray;
        headerRow.Cells[0].AddParagraph(header1);
        headerRow.Cells[1].AddParagraph(header2);

        var dataRow = table.AddRow();
        AddPlainCellText(dataRow.Cells[0], value1);
        AddPlainCellText(dataRow.Cells[1], value2);
    }

    private static void AddLegalClauses(Section section, DeviceTermContent content)
    {
        foreach (var clause in content.Clauses)
        {
            var p = section.AddParagraph();
            p.Format.SpaceAfter = Unit.FromPoint(4);
            p.Format.LeftIndent = Unit.FromCentimeter(0.4);
            p.AddText("•  ");
            p.AddText(ResponsibilityTermText.RenderClause(clause, content.Indemnity));
        }
    }

    /// <summary>
    /// The 3 photos frozen for this exact item at Term-generation time (never
    /// re-read live afterward — see <see cref="ResponsibilityTermEquipmentItem.PhotoSet"/>'s
    /// remarks). Rendered as 3 equal-size images in one row; if they would
    /// not fit legibly on the remaining space of the current page, MigraDoc's
    /// normal page flow simply carries the table onto a new page, which keeps
    /// the same header/logo styling automatically (the header/footer are
    /// section-wide, not per-page content).
    /// </summary>
    private static void AddPhotoSection(Section section, ResponsibilityTermEquipmentItem item, List<string> tempFiles)
    {
        if (item.PhotoSet is not { } photoSet)
        {
            return;
        }

        // Kept with the photo row, so the heading never sits alone at the
        // bottom of a page while the photos start the next one.
        var heading = section.AddParagraph("REGISTO FOTOGRÁFICO DO EQUIPAMENTO", "SectionHeading");
        heading.Format.KeepWithNext = true;

        var table = section.AddTable();
        table.Borders.Visible = false;
        for (var i = 0; i < photoSet.Photos.Count; i++)
        {
            table.AddColumn(Unit.FromCentimeter(5.4));
        }

        var row = table.AddRow();
        for (var i = 0; i < photoSet.Photos.Count; i++)
        {
            var detected = ImageSignatureDetector.Detect(photoSet.Photos[i]);
            var extension = detected switch
            {
                ImageContentType.Png => "png",
                ImageContentType.WebP => "webp",
                _ => "jpg",
            };
            var path = WriteTempImageFile(photoSet.Photos[i], extension, tempFiles);
            var image = row.Cells[i].AddImage(path);
            image.LockAspectRatio = true;
            image.Width = Unit.FromCentimeter(5.1);
        }

        var caption = section.AddParagraph();
        caption.Format.Font.Size = 8;
        caption.Format.Font.Italic = true;
        caption.Format.SpaceBefore = Unit.FromPoint(4);
        caption.Format.SpaceAfter = Unit.FromPoint(10);
        caption.AddText("Fotografias de referência do estado do equipamento.");
        caption.AddLineBreak();
        caption.AddText($"Registadas em: {DocumentCulture.FormatDate(DateOnly.FromDateTime(photoSet.CapturedAtUtc))}");
    }

    private static string JoinNonBlank(params string?[] values) =>
        string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v)));

    private static string WriteTempImageFile(byte[] bytes, string extension, List<string> tempFiles)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rt-{Guid.NewGuid():N}.{extension}");
        File.WriteAllBytes(path, bytes);
        tempFiles.Add(path);
        return path;
    }

    /// <param name="document">The document to style.</param>
    /// <param name="accentColor">The client's #RRGGBB accent for Template V2's headings; null for V1, which has none.</param>
    private static void DefineStyles(Document document, string? accentColor)
    {
        var normal = document.Styles["Normal"]!;
        normal.Font.Name = LiberationFontResolver.FamilyName;
        normal.Font.Size = 10;

        var heading = document.Styles.AddStyle("TermHeading", "Normal");
        heading.Font.Size = 16;
        heading.Font.Bold = true;
        heading.ParagraphFormat.Alignment = ParagraphAlignment.Center;
        heading.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);

        var sectionHeading = document.Styles.AddStyle("SectionHeading", "Normal");
        sectionHeading.Font.Size = 12;
        sectionHeading.Font.Bold = true;
        sectionHeading.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
        sectionHeading.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);

        if (accentColor is null)
        {
            return;
        }

        // The client's accent colour (validated as #RRGGBB when the profile
        // is loaded) — used only by Template V2.
        var accent = new Color(
            byte.Parse(accentColor.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(accentColor.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(accentColor.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

        var termHeadingAccent = document.Styles.AddStyle("TermHeadingAccent", "TermHeading");
        termHeadingAccent.Font.Color = accent;

        var sectionHeadingAccent = document.Styles.AddStyle("SectionHeadingAccent", "SectionHeading");
        sectionHeadingAccent.Font.Size = 14;
        sectionHeadingAccent.Font.Color = accent;
        sectionHeadingAccent.ParagraphFormat.SpaceBefore = Unit.FromPoint(18);
    }

    private static void AddFooter(Section section, ResponsibilityTermDocumentModel model)
    {
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 8;
        footer.AddText($"{model.DocumentNumber} · v{model.Version} · Página ");
        footer.AddPageField();
        footer.AddText(" de ");
        footer.AddNumPagesField();
    }

    private static void AddTitleAndIssuer(Section section, ResponsibilityTermDocumentModel model, DocumentIssuerOptions issuer)
    {
        // A logo/letterhead can be added later (Phase 6 scope) by
        // inserting an image here from a controlled local/private asset —
        // never a fabricated one and never loaded from a remote URL.
        if (issuer.IsConfigured)
        {
            var issuerParagraph = section.AddParagraph();
            issuerParagraph.Format.Font.Size = 9;
            issuerParagraph.Format.SpaceAfter = Unit.FromPoint(10);
            issuerParagraph.AddText(issuer.LegalName!);
            issuerParagraph.AddLineBreak();
            issuerParagraph.AddText(issuer.Address!);
            issuerParagraph.AddLineBreak();
            issuerParagraph.AddText($"NIF/NIPC: {issuer.TaxId}");
        }

        section.AddParagraph("TERMO DE RESPONSABILIDADE", "TermHeading");

        var identification = section.AddParagraph();
        identification.Format.Alignment = ParagraphAlignment.Center;
        identification.Format.SpaceAfter = Unit.FromPoint(14);
        identification.AddText($"Documento nº {model.DocumentNumber} · Versão {model.Version}");
    }

    private static void AddDeliveryInfo(Section section, ResponsibilityTermDocumentModel model)
    {
        section.AddParagraph("Dados da Entrega", "SectionHeading");
        var p = section.AddParagraph();
        p.AddFormattedText("Data de entrega: ", TextFormat.Bold);
        p.AddText(model.DeliveryDate is { } deliveryDate ? DocumentCulture.FormatDate(deliveryDate) : "-");
        p.AddLineBreak();
        p.AddFormattedText("Entregue por: ", TextFormat.Bold);
        p.AddText(model.DeliveredByDisplayName);
        p.AddLineBreak();
        p.AddFormattedText("Número de equipamentos: ", TextFormat.Bold);
        p.AddText(model.EquipmentItems.Count.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Template V2's header facts. No delivery date and no "Entregue por": a
    /// carrier term can be prepared before the equipment is received, so the
    /// document never states that delivery already happened — the reception
    /// date is the consultant's own entry in the final signing block.
    /// </summary>
    private static void AddDeliveryInfoV2(Section section, ResponsibilityTermDocumentModel model)
    {
        section.AddParagraph("Dados da Entrega", "SectionHeading");
        var p = section.AddParagraph();
        p.AddFormattedText("Responsável pela entrega: ", TextFormat.Bold);
        p.AddText(model.DeliveredByDisplayName);
        p.AddLineBreak();
        p.AddFormattedText("Número de equipamentos: ", TextFormat.Bold);
        p.AddText(model.EquipmentItems.Count.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddConsultantSection(Section section, ResponsibilityTermConsultantInfo consultant)
    {
        section.AddParagraph("Dados do Consultor", "SectionHeading");
        var p = section.AddParagraph();
        p.AddFormattedText("Nome: ", TextFormat.Bold);
        p.AddText(consultant.FullName);
        p.AddLineBreak();
        p.AddFormattedText("NIF: ", TextFormat.Bold);
        p.AddText(consultant.Nif);
        p.AddLineBreak();
        p.AddFormattedText("Morada: ", TextFormat.Bold);
        p.AddText(consultant.Address);
        p.AddLineBreak();
        p.AddFormattedText("Telefone: ", TextFormat.Bold);
        p.AddText(consultant.Phone);

        if (!string.IsNullOrWhiteSpace(consultant.Email))
        {
            p.AddLineBreak();
            p.AddFormattedText("Email: ", TextFormat.Bold);
            p.AddText(consultant.Email);
        }
    }

    /// <summary>Omitted entirely when the handover's consultant had no active allocation at finalization — never rendered as a blank/meaningless section (Phase 6 scope).</summary>
    private static void AddProfessionalContextSection(Section section, ResponsibilityTermProfessionalContext context)
    {
        section.AddParagraph("Contexto Profissional", "SectionHeading");
        var p = section.AddParagraph();
        p.AddFormattedText("Tipo de colaboração: ", TextFormat.Bold);
        p.AddText(TranslateWorkforceType(context.WorkforceType));
        p.AddLineBreak();
        p.AddFormattedText("Vínculo: ", TextFormat.Bold);
        p.AddText(TranslateEngagementType(context.EngagementType));
        p.AddLineBreak();
        p.AddFormattedText("Função: ", TextFormat.Bold);
        p.AddText(context.RoleOrFunction);

        if (!string.IsNullOrWhiteSpace(context.CompanyName))
        {
            p.AddLineBreak();
            p.AddFormattedText("Empresa: ", TextFormat.Bold);
            p.AddText(context.CompanyName);
        }
    }

    /// <summary>
    /// One row per item; laptops get an extra merged sub-row for their
    /// specifications so generic equipment never shows empty laptop-only
    /// columns (Phase 6 scope). The header row's
    /// <see cref="Row.HeadingFormat"/> makes MigraDoc repeat it on every page
    /// the table flows onto (Phase 6 scope). Columns use the full
    /// page content width (17 cm inside the 2 cm margins) rather than an
    /// arbitrary narrower total. Only the two genuine machine identifiers
    /// (Asset Tag, Nº de Série) go through <see cref="AddWrappableCellText"/>
    /// — MigraDoc only wraps at existing whitespace, so a real-world serial
    /// value with no spaces (verified empirically: a plain unbroken string
    /// overflowed its column and visually overlapped the next one) needs
    /// inserted invisible break points to stay inside its own column instead
    /// of corrupting a legal document's layout. Tipo/Fabricante/Modelo are
    /// human-readable labels, rendered exactly as given via
    /// <see cref="AddPlainCellText"/> — never worth risking an injected
    /// character in a name for a cosmetic wrap that space-containing values
    /// essentially never need.
    /// </summary>
    private static void AddEquipmentTable(Section section, IReadOnlyList<ResponsibilityTermEquipmentItem> items, string heading = "Equipamentos Entregues")
    {
        section.AddParagraph(heading, "SectionHeading");

        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.Gray;
        table.AddColumn(Unit.FromCentimeter(2.6));
        table.AddColumn(Unit.FromCentimeter(2.4));
        table.AddColumn(Unit.FromCentimeter(3.7));
        table.AddColumn(Unit.FromCentimeter(3.7));
        table.AddColumn(Unit.FromCentimeter(3.6));

        var headerRow = table.AddRow();
        headerRow.HeadingFormat = true;
        headerRow.Format.Font.Bold = true;
        headerRow.Shading.Color = Colors.LightGray;
        headerRow.Cells[0].AddParagraph("Asset Tag");
        headerRow.Cells[1].AddParagraph("Tipo");
        headerRow.Cells[2].AddParagraph("Fabricante");
        headerRow.Cells[3].AddParagraph("Modelo");
        headerRow.Cells[4].AddParagraph("Nº de Série");

        foreach (var item in items)
        {
            var row = table.AddRow();
            AddWrappableCellText(row.Cells[0], item.AssetTag);
            AddPlainCellText(row.Cells[1], item.EquipmentTypeName);
            AddPlainCellText(row.Cells[2], item.Manufacturer ?? "-");
            AddPlainCellText(row.Cells[3], item.Model ?? "-");
            AddWrappableCellText(row.Cells[4], item.SerialNumber ?? "-");

            if (item.LaptopSpecification is { } spec)
            {
                var specRow = table.AddRow();
                var specCell = specRow.Cells[0];
                specCell.MergeRight = 4;
                var specParagraph = specCell.AddParagraph();
                specParagraph.Format.Font.Size = 8;
                specParagraph.Format.Font.Italic = true;
                specParagraph.AddText(
                    $"Especificações: Processador: {spec.Processor} · Memória: {spec.Memory} · Armazenamento: {spec.Storage}");
            }
        }
    }

    /// <summary>
    /// For genuine machine identifiers only (asset tag, serial number, IMEI,
    /// SIM serial, mobile number) — never for human-readable text (names,
    /// manufacturer, model, notes, dates), which must render exactly as
    /// stored and never receive an injected break character at all (see
    /// <see cref="AddPlainCellText"/>). An identifier's displayed characters
    /// must always be able to reconstruct the exact original value, which
    /// <see cref="WithBreakOpportunities"/>'s invisible break points
    /// guarantee — unlike a visible hyphen, which would not.
    /// </summary>
    private static void AddWrappableCellText(Cell cell, string text) => cell.AddParagraph(WithBreakOpportunities(text));

    /// <summary>
    /// For human-readable text — renders the value completely unchanged, with
    /// no injected break characters of any kind. MigraDoc already wraps at
    /// this value's own natural whitespace; a name, label, or date is never
    /// long/unbroken enough in practice to need more than that, and unlike a
    /// machine identifier, injecting anything into it — even an invisible
    /// character — risks corrupting text a reader might copy elsewhere.
    /// </summary>
    private static void AddPlainCellText(Cell cell, string text) => cell.AddParagraph(text);

    /// <summary>
    /// Inserts an invisible break opportunity (<see cref="InvisibleBreak"/>)
    /// every 10 characters of any run with no whitespace/hyphen/slash —
    /// MigraDoc only breaks lines at existing whitespace or an inserted break
    /// character, so a long unbroken value (an asset tag, serial number, or
    /// IMEI/SIM serial with no spaces) would otherwise overflow its table
    /// column and overlap the next one, rather than wrapping onto a second
    /// line within its own cell. Never inserts as the very last character of
    /// the value — a trailing break point has nothing left to wrap before, so
    /// it would only ever be dead weight, and (per the empirical finding on
    /// <see cref="InvisibleBreak"/>) a *visible* separator in that position is
    /// exactly the dangling-hyphen defect this exists to prevent. Values
    /// short enough to fit a column, or containing normal spaces/hyphens, are
    /// returned unchanged in effect — the inserted points only matter once a
    /// line actually needs to break, and even then draw nothing.
    /// </summary>
    internal static string WithBreakOpportunities(string text)
    {
        const int chunkSize = 10;
        var builder = new System.Text.StringBuilder(text.Length + (text.Length / chunkSize));
        var runLength = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            builder.Append(ch);

            if (char.IsWhiteSpace(ch) || ch is '-' or '/')
            {
                runLength = 0;
                continue;
            }

            runLength++;
            if (runLength == chunkSize && i < text.Length - 1)
            {
                builder.Append(InvisibleBreak);
                runLength = 0;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Deliberately neutral, operational wording — no invented financial
    /// liability or penalty clauses (Phase 6 scope). Delivery
    /// evidence photos are referenced, never embedded (Phase 6 scope).
    /// </summary>
    private static void AddDeclaration(Section section)
    {
        section.AddParagraph("Declaração de Responsabilidade", "SectionHeading");
        var p = section.AddParagraph();
        p.Format.SpaceAfter = Unit.FromPoint(10);
        p.AddText(ResponsibilityDeclarationTextV1);

        var evidence = section.AddParagraph();
        evidence.Format.Font.Size = 9;
        evidence.Format.Font.Italic = true;
        evidence.AddText("Evidências fotográficas da inspeção de entrega encontram-se registadas no sistema.");
    }

    /// <summary>
    /// A visual placeholder only — no interactive form field, no signature
    /// capture. Prepares layout space for a future digital-signature workflow
    /// without implementing one now (Phase 6 scope).
    /// </summary>
    private static void AddSignatureBlock(Section section)
    {
        var line = section.AddParagraph();
        line.Format.SpaceBefore = Unit.FromCentimeter(1.5);
        line.Format.SpaceAfter = Unit.FromPoint(6);
        line.AddText("Assinatura do(a) Consultor(a): _______________________________________________");

        var dateLine = section.AddParagraph();
        dateLine.AddText("Data: _____ / _____ / _________");
    }

    /// <summary>
    /// Template V2's final signing block: the consultant's signature and the
    /// one consultant-facing "Data de receção dos equipamentos" field, always
    /// left blank — the consultant completes it when signing, after receiving
    /// and checking the equipment (a carrier term may be prepared before
    /// receipt, and prefilling only some terms would give the field two
    /// meanings). V1's <see cref="AddSignatureBlock"/> is left untouched so V1
    /// renders exactly as before. Laid out with real writing space. Every paragraph is kept with
    /// the next one, so label, signature line, date and the generated-document
    /// note always move to a new page together rather than leaving an orphan
    /// label behind: the block is therefore always on the Term's last page.
    /// </summary>
    private static void AddFinalSigningBlock(Section section)
    {
        var label = section.AddParagraph();
        label.Format.SpaceBefore = Unit.FromCentimeter(1.5);
        label.Format.KeepWithNext = true;
        label.AddText("Assinatura do(a) Consultor(a):");

        var signatureLine = section.AddParagraph();
        signatureLine.Format.SpaceBefore = Unit.FromCentimeter(1.2);
        signatureLine.Format.KeepWithNext = true;
        signatureLine.AddText("____________________________________________________");

        var dateLabel = section.AddParagraph();
        dateLabel.Format.SpaceBefore = Unit.FromCentimeter(1);
        dateLabel.Format.KeepWithNext = true;
        dateLabel.AddText("Data de receção dos equipamentos:");

        var dateLine = section.AddParagraph();
        dateLine.Format.SpaceBefore = Unit.FromCentimeter(0.8);
        dateLine.Format.KeepWithNext = true;
        dateLine.AddText("____ / ____ / ______");
    }

    private static void AddMetadataFooterNote(Section section, ResponsibilityTermDocumentModel model)
    {
        var note = section.AddParagraph();
        note.Format.SpaceBefore = Unit.FromCentimeter(1);
        note.Format.Font.Size = 7;
        note.Format.Font.Color = Colors.Gray;
        note.AddText($"Documento gerado em {DocumentCulture.FormatDateTimeUtc(model.GeneratedAtUtc)} por {model.GeneratedByDisplayName}.");
    }

    private static string TranslateWorkforceType(string value) => value switch
    {
        "Internal" => "Interno",
        "External" => "Externo",
        _ => value,
    };

    private static string TranslateEngagementType(string value) => value switch
    {
        "ServiceProvider" => "Prestador de Serviço",
        "EmploymentContract" => "Contrato de Trabalho",
        _ => value,
    };
}
