namespace EquipmentManagement.Infrastructure.Email;

/// <summary>
/// Generic SMTP configuration (Phase 7 scope) — bound from
/// the "Email:Smtp" configuration section. Deliberately provider-agnostic:
/// any SMTP-speaking provider (self-hosted, Microsoft 365, SendGrid,
/// Postmark, SES's SMTP interface, ...) can sit behind this without any code
/// change. No default value for any field — same fail-safe discipline as
/// <c>Documents.DocumentIssuerOptions</c>: an unconfigured sender must fail
/// clearly (see <see cref="SmtpEmailSender"/>), never silently pretend to
/// send. Real values are supplied only via environment variables or a secret
/// manager, never committed (see <c>.env.example</c>'s obviously-fake
/// placeholders).
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email:Smtp";

    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary>
    /// How the connection negotiates TLS — required, explicit, and never
    /// silently downgradable (Phase 7 hardening pass): unlike the
    /// old boolean it replaces, <see cref="EmailTlsMode.StartTls"/>
    /// *requires* the STARTTLS upgrade to succeed rather than merely
    /// attempting it, so a misconfigured/compromised connection fails loudly
    /// instead of silently falling back to plaintext.
    /// </summary>
    public EmailTlsMode TlsMode { get; set; } = EmailTlsMode.StartTls;

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>An API caller can never choose the From address (Phase 7 scope) — it is always this configured value.</summary>
    public string? FromAddress { get; set; }

    public string? FromDisplayName { get; set; }

    public string? ReplyToAddress { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>
/// Maps directly to <see cref="MailKit.Security.SecureSocketOptions"/> — kept
/// as this codebase's own enum (rather than binding configuration straight to
/// MailKit's type) so <see cref="EmailOptions"/> itself stays free of any
/// MailKit reference; only <c>SmtpEmailSender</c> knows about MailKit.
/// </summary>
public enum EmailTlsMode
{
    /// <summary>No TLS at all — only appropriate for a trusted local/dev relay (e.g. a loopback test SMTP catcher), never a real deployment.</summary>
    None,

    /// <summary>Connect in plaintext, then require the STARTTLS upgrade to succeed before authenticating/sending — the common mode for port 587. Never falls back to plaintext if the upgrade fails.</summary>
    StartTls,

    /// <summary>TLS from the first byte of the connection (implicit TLS) — the common mode for port 465.</summary>
    SslOnConnect,
}
