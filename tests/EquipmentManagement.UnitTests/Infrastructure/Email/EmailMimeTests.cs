using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EquipmentManagement.Infrastructure.Email;
using MimeKit;

namespace EquipmentManagement.UnitTests.Infrastructure.Email;

/// <summary>
/// The exact message <see cref="SmtpEmailSender"/> sends, serialized with the
/// same wire format and parsed back with MimeKit: Subjects and names survive
/// as the intended Unicode, the wire is 7-bit clean, the sender identity,
/// Reply-To and signature come from configuration, and no value can form a
/// header. Synthetic identities only.
/// </summary>
public class EmailMimeTests
{
    private static readonly EmailOptions Smtp = new()
    {
        Host = "smtp.example.test",
        FromAddress = "rh.ficticia@example.test",
        FromDisplayName = "Joana Demonstração Fictícia",
    };

    private static readonly EmailSignatureOptions Signature = new()
    {
        Name = "Joana Demonstração Fictícia",
        Role = "Técnica de Recursos Humanos",
        Company = "Acme",
    };

    public static TheoryData<string> EveryOperationalSubject()
    {
        var line = new DispatchEquipmentLine("LP-0001", "Portátil", "Dell", "Latitude", "SN1", null);
        return new TheoryData<string>
        {
            ResponsibilityTermEmailTemplate.Build("TR-2026-000123", "Consultor Fictício", ["Portátil · Dell · LP-0001"]).Subject,
            EquipmentDispatchEmailTemplate.Build("Consultor Fictício", [line], new DateOnly(2026, 10, 20), null).Subject,
            EquipmentDispatchEmailTemplate.Build("Consultor Fictício", [line, line], new DateOnly(2026, 10, 20), null).Subject,
            ConsultantAllocationCollectionEmailTemplate.Build("Consultor Fictício", [line], new DateOnly(2026, 10, 20), null).Subject,
            ConsultantAllocationCollectionEmailTemplate.Build("Consultor Fictício", [line, line], new DateOnly(2026, 10, 20), null).Subject,
            EquipmentRetirementRequestEmailTemplate.Build("LP-0001", "Portátil", null, null, null, "Motivo", "Ana", DateTime.UtcNow, null).Subject,
            // Synthetic: every Portuguese character and punctuation mark the matrix requires.
            "Ação: á à â ã é ê í ó ô õ ú ç º ª – - teste",
        };
    }

    [Theory]
    [MemberData(nameof(EveryOperationalSubject))]
    public void Subject_SurvivesTheWireAsTheExactUnicodeText_WithNoEntitiesOrMojibake(string subject)
    {
        Assert.DoesNotMatch(new Regex("&[#a-zA-Z0-9]+;"), subject);
        Assert.DoesNotMatch(new Regex("Ã[\u0080-¿]|â€|�"), subject);

        var (wire, parsed) = RoundTrip(new EmailMessage("consultor@example.test", subject, "Olá"));

        Assert.Equal(subject, parsed.Subject);
        AssertSevenBitClean(wire);
    }

    [Fact]
    public void TheRealSubjects_AreExactlyTheApprovedText()
    {
        var line = new DispatchEquipmentLine("LP-0001", "Portátil", "Dell", "Latitude", "SN1", null);
        Assert.Equal("Termo de Responsabilidade de Equipamentos: TR-2026-000123",
            ResponsibilityTermEmailTemplate.Build("TR-2026-000123", "X", []).Subject);
        Assert.Equal("Entrega de equipamento agendada",
            EquipmentDispatchEmailTemplate.Build("X", [line], new DateOnly(2026, 10, 20), null).Subject);
        Assert.Equal("Recolha de equipamentos agendada",
            ConsultantAllocationCollectionEmailTemplate.Build("X", [line, line], new DateOnly(2026, 10, 20), null).Subject);
        Assert.Equal("Nova solicitação de abate: LP-0001",
            EquipmentRetirementRequestEmailTemplate.Build("LP-0001", "Portátil", null, null, null, "M", "A", DateTime.UtcNow, null).Subject);
    }

    [Fact]
    public void From_IsTheConfiguredSender_AndReplyToDefaultsToIt()
    {
        var (_, parsed) = RoundTrip(new EmailMessage("consultor@example.test", "Assunto", "Olá"));

        var from = Assert.Single(parsed.From.Mailboxes);
        Assert.Equal("Joana Demonstração Fictícia", from.Name);
        Assert.Equal("rh.ficticia@example.test", from.Address);
        Assert.Equal("rh.ficticia@example.test", Assert.Single(parsed.ReplyTo.Mailboxes).Address);
    }

    [Fact]
    public void ReplyTo_UsesTheConfiguredAddress_WhenOneIsSet()
    {
        var smtp = new EmailOptions { Host = Smtp.Host, FromAddress = Smtp.FromAddress, FromDisplayName = Smtp.FromDisplayName, ReplyToAddress = "rh.equipa@example.test" };

        var (_, parsed) = RoundTrip(new EmailMessage("consultor@example.test", "Assunto", "Olá"), smtp);

        Assert.Equal("rh.equipa@example.test", Assert.Single(parsed.ReplyTo.Mailboxes).Address);
    }

    [Fact]
    public void Body_IsQuotedPrintableUtf8_AndCarriesTheSignatureExactlyOnce()
    {
        var body = ResponsibilityTermEmailTemplate.Build("TR-2026-000123", "Consultor Fictício", ["Portátil · Dell · LP-0001"]).Body;

        var (wire, parsed) = RoundTrip(new EmailMessage("consultor@example.test", "Assunto", body, IncludeSignature: true));

        var text = Assert.Single(parsed.BodyParts.OfType<TextPart>());
        Assert.Equal("utf-8", text.ContentType.Charset);
        Assert.Equal(ContentEncoding.QuotedPrintable, text.ContentTransferEncoding);
        var received = Normalize(parsed.TextBody);
        Assert.Equal(Normalize(body.TrimEnd() + "\n\nJoana Demonstração Fictícia\nTécnica de Recursos Humanos\nAcme"), received);
        Assert.Contains("😊", received, StringComparison.Ordinal);
        Assert.EndsWith("Obrigada!\n\nJoana Demonstração Fictícia\nTécnica de Recursos Humanos\nAcme", received, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(received, "Técnica de Recursos Humanos"));
        AssertSevenBitClean(wire);
    }

    [Fact]
    public void Signature_IsOmitted_WhenNotRequested_OrNotConfigured()
    {
        var (_, unsigned) = RoundTrip(new EmailMessage("a@example.test", "Assunto", "Olá"));
        Assert.Equal("Olá", Normalize(unsigned.TextBody).TrimEnd());

        var (_, unconfigured) = RoundTrip(new EmailMessage("a@example.test", "Assunto", "Olá", IncludeSignature: true), signature: new EmailSignatureOptions());
        Assert.Equal("Olá", Normalize(unconfigured.TextBody).TrimEnd());
    }

    [Fact]
    public void Signature_KeepsOrdinarySpecialCharacters_Literally()
    {
        var signature = new EmailSignatureOptions { Name = "Ana <Teste> & \"Ç\"", Role = "RH & Pessoas", Company = "Acme" };

        var (_, parsed) = RoundTrip(new EmailMessage("a@example.test", "Assunto", "Olá", IncludeSignature: true), signature: signature);

        Assert.EndsWith("Ana <Teste> & \"Ç\"\nRH & Pessoas\nAcme", Normalize(parsed.TextBody).TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void CrLfInASubjectOrDisplayName_NeverBecomesAHeader()
    {
        var smtp = new EmailOptions { Host = Smtp.Host, FromAddress = Smtp.FromAddress, FromDisplayName = "RH\r\nBcc: intruso@example.test" };

        var (_, parsed) = RoundTrip(new EmailMessage("a@example.test", "Assunto\r\nBcc: intruso@example.test", "Olá"), smtp);

        Assert.Empty(parsed.Bcc);
        Assert.DoesNotContain(parsed.Headers, h => h.Field.Equals("Bcc", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("rh.ficticia@example.test", Assert.Single(parsed.From.Mailboxes).Address);
    }

    [Fact]
    public void Attachment_ArrivesByteForByte()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% sintético\n%%EOF").Concat(Enumerable.Range(0, 2048).Select(i => (byte)(i % 256))).ToArray();

        var (_, parsed) = RoundTrip(new EmailMessage("a@example.test", "Assunto", "Olá", new EmailAttachment("termo.pdf", "application/pdf", pdf), IncludeSignature: true));

        var attachment = Assert.Single(parsed.Attachments.OfType<MimePart>());
        Assert.Equal("termo.pdf", attachment.FileName);
        using var decoded = new MemoryStream();
        attachment.Content!.DecodeTo(decoded);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(pdf)), Convert.ToHexString(SHA256.HashData(decoded.ToArray())));
    }

    private static (byte[] Wire, MimeMessage Parsed) RoundTrip(EmailMessage message, EmailOptions? smtp = null, EmailSignatureOptions? signature = null)
    {
        using var built = SmtpEmailSender.BuildMimeMessage(message, smtp ?? Smtp, signature ?? Signature);
        using var stream = new MemoryStream();
        built.WriteTo(SmtpEmailSender.WireFormat, stream);
        var wire = stream.ToArray();
        return (wire, MimeMessage.Load(new MemoryStream(wire)));
    }

    /// <summary>Every byte on the wire is 7-bit ASCII: no relay or client can re-interpret it.</summary>
    private static void AssertSevenBitClean(byte[] wire) => Assert.All(wire, b => Assert.True(b < 0x80, $"Non-ASCII byte 0x{b:X2} on the wire."));

    private static string Normalize(string? text) => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
}
