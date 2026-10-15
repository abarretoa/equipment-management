using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EquipmentManagement.Infrastructure.Email;

/// <summary>
/// Generic SMTP implementation of <see cref="IEmailSender"/> using MailKit
/// (Phase 7 hardening pass; ADR-054). Replaces the original
/// <see cref="System.Net.Mail.SmtpClient"/>-based implementation, which
/// Microsoft's own documentation explicitly does not recommend for new
/// development (no modern authentication mechanisms, limited protocol
/// support). Real (non-test) sends in every environment other than "Testing"
/// go through this class — see <c>Infrastructure.DependencyInjection</c>.
///
/// Never disables certificate validation: <see cref="MailKit.Net.Smtp.SmtpClient"/>'s
/// default certificate validation is left untouched — no
/// <c>ServerCertificateValidationCallback</c> override exists anywhere in
/// this class (Phase 7 hardening pass).
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, IOptions<EmailSignatureOptions> signatureOptions) : IEmailSender
{
    /// <summary>
    /// Every message is serialized 7-bit clean: headers (Subject, display
    /// names) as RFC 2047 UTF-8 encoded-words — never raw UTF-8 headers,
    /// even when a server advertises SMTPUTF8 — and the plain-text body as
    /// quoted-printable UTF-8. So no relay or client along the way can
    /// re-interpret the bytes. Subjects stay ordinary Unicode in source;
    /// MimeKit does all the encoding.
    /// </summary>
    internal static readonly FormatOptions WireFormat = CreateWireFormat();

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            throw new EmailSendException(
                EmailSendFailureReason.NotConfigured, "Email delivery is not configured (Email:Smtp:Host/FromAddress).");
        }

        using var mimeMessage = BuildMimeMessage(message, settings, signatureOptions.Value);

        using var client = new MailKit.Net.Smtp.SmtpClient();

        try
        {
            await client.ConnectAsync(settings.Host!, settings.Port, ToSecureSocketOptions(settings.TlsMode), cancellationToken);

            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(WireFormat, mimeMessage, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (AuthenticationException ex)
        {
            throw new EmailSendException(EmailSendFailureReason.Rejected, "The mail provider rejected the configured credentials.", ex);
        }
        catch (SmtpCommandException ex)
        {
            throw new EmailSendException(ClassifySmtpCommandFailure(ex), "The mail provider rejected the send request.", ex);
        }
        catch (SmtpProtocolException ex)
        {
            throw new EmailSendException(EmailSendFailureReason.Temporary, "The mail provider connection failed unexpectedly.", ex);
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            throw new EmailSendException(EmailSendFailureReason.Temporary, "The mail provider could not be reached.", ex);
        }
        catch (IOException ex)
        {
            throw new EmailSendException(EmailSendFailureReason.Temporary, "The connection to the mail provider was interrupted.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EmailSendException(EmailSendFailureReason.Unknown, "Sending the email failed unexpectedly.", ex);
        }
    }

    /// <summary>
    /// Builds the exact message sent (exposed for tests). From is the
    /// configured sender identity, never a caller's choice; Reply-To is the
    /// configured address or, when none is set, the sender mailbox itself.
    /// The corporate signature is appended once, here, only to messages that
    /// ask for it. Typed <see cref="MailboxAddress"/> values and MimeKit's
    /// header encoding keep CR/LF in any value from ever forming a header.
    /// </summary>
    internal static MimeMessage BuildMimeMessage(EmailMessage message, EmailOptions settings, EmailSignatureOptions signature)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(settings.FromDisplayName?.Trim() ?? string.Empty, settings.FromAddress!.Trim()));
        mimeMessage.To.Add(MailboxAddress.Parse(message.ToAddress));
        var replyTo = string.IsNullOrWhiteSpace(settings.ReplyToAddress) ? settings.FromAddress! : settings.ReplyToAddress;
        mimeMessage.ReplyTo.Add(MailboxAddress.Parse(replyTo.Trim()));

        mimeMessage.Subject = message.Subject;

        var text = message.IncludeSignature ? EmailSignature.AppendTo(message.PlainTextBody, signature) : message.PlainTextBody;
        var bodyBuilder = new BodyBuilder { TextBody = text };
        if (message.Attachment is { } attachment)
        {
            bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mimeMessage.Body = bodyBuilder.ToMessageBody();
        foreach (var part in mimeMessage.BodyParts.OfType<TextPart>().Where(p => !p.IsAttachment))
        {
            part.ContentType.Charset = "utf-8";
            part.ContentTransferEncoding = ContentEncoding.QuotedPrintable;
        }

        return mimeMessage;
    }

    private static FormatOptions CreateWireFormat()
    {
        var format = FormatOptions.Default.Clone();
        format.International = false;
        return format;
    }

    private static SecureSocketOptions ToSecureSocketOptions(EmailTlsMode mode) => mode switch
    {
        EmailTlsMode.None => SecureSocketOptions.None,
        EmailTlsMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.StartTls,
    };

    /// <summary>
    /// A permanent (5xx) SMTP reply means the provider rejected this attempt
    /// outright; a transient (4xx) reply means it may succeed on retry. Falls
    /// back to <see cref="EmailSendFailureReason.Rejected"/> for the specific
    /// recipient/sender/message-rejection error codes MailKit already
    /// classifies, and to <see cref="EmailSendFailureReason.Temporary"/>
    /// otherwise, matching the numeric SMTP reply-code convention.
    /// </summary>
    private static EmailSendFailureReason ClassifySmtpCommandFailure(SmtpCommandException ex) => ex.ErrorCode switch
    {
        SmtpErrorCode.RecipientNotAccepted or SmtpErrorCode.SenderNotAccepted or SmtpErrorCode.MessageNotAccepted => EmailSendFailureReason.Rejected,
        _ => (int)ex.StatusCode >= 500 ? EmailSendFailureReason.Rejected : EmailSendFailureReason.Temporary,
    };
}
