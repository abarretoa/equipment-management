using System.Globalization;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Documents;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Files;
using EquipmentManagement.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace EquipmentManagement.Infrastructure.Hosting;

/// <summary>
/// Refuses to start a Production instance that could not do its job: one that
/// would print a placeholder issuer on a Responsibility Term, could never send
/// an email, would link to nowhere, or would keep evidence files and session
/// keys inside the container filesystem. Runs only when the environment is
/// exactly "Production"; Development, Testing and the demo build keep their
/// permissive defaults.
/// </summary>
/// <remarks>
/// Checks configuration shape only. It never opens a connection to the SMTP
/// server, so a mail outage cannot stop the API from starting. Messages name
/// settings, never their values.
/// </remarks>
public static class ProductionConfigurationValidator
{
    /// <summary>
    /// Text that only ever appears in an example file. "REPLACE" is matched
    /// separately with its exact casing (the marker used in the .env examples)
    /// so that an ordinary word containing "replace" is not mistaken for it.
    /// </summary>
    private static readonly string[] PlaceholderMarkers =
        ["Example Consulting", "changeme", "change-me", "change me", "example.invalid", "Replace-With", "placeholder"];

    public static void ThrowIfInvalid(IConfiguration configuration)
    {
        var problems = Validate(configuration);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Production configuration is invalid; refusing to start. Fix the following settings " +
                "(see deploy/production/README.md):" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => " - " + p)));
        }
    }

    public static IReadOnlyList<string> Validate(IConfiguration configuration)
    {
        var problems = new List<string>();

        ValidateIssuer(configuration, problems);
        ValidateSmtp(configuration, problems);
        ValidatePublicBaseUrl(configuration, problems);
        ValidateBootstrapAdmin(configuration, problems);
        AddIfNotNull(problems, WritableDirectory.Check(
            configuration[$"{PrivateStorageOptions.SectionName}:{nameof(PrivateStorageOptions.RootPath)}"], "PrivateStorage__RootPath"));
        AddIfNotNull(problems, WritableDirectory.Check(
            configuration[DataProtectionKeyRingOptions.KeyRingPathKey], "DataProtection__KeyRingPath"));

        return problems;
    }

    /// <summary>True when <paramref name="value"/> carries text that only an unedited example file would contain.</summary>
    public static bool LooksLikePlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains("REPLACE", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var marker in PlaceholderMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // ".invalid" is reserved (RFC 2606) and can never be a real mail or web host.
        return value.TrimEnd('/', ' ').EndsWith(".invalid", StringComparison.OrdinalIgnoreCase)
            || value.Contains(".invalid/", StringComparison.OrdinalIgnoreCase)
            || value.Contains(".invalid:", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateIssuer(IConfiguration configuration, List<string> problems)
    {
        var section = configuration.GetSection(DocumentIssuerOptions.SectionName);
        foreach (var field in new[] { nameof(DocumentIssuerOptions.LegalName), nameof(DocumentIssuerOptions.Address), nameof(DocumentIssuerOptions.TaxId) })
        {
            var value = section[field];
            var name = $"Documents__Issuer__{field}";
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"{name} is not set.");
            }
            else if (LooksLikePlaceholder(value)
                || (field == nameof(DocumentIssuerOptions.TaxId) && value.Trim().All(c => c == '0')))
            {
                // An all-zero tax ID is the value the example files ship with.
                problems.Add($"{name} still holds placeholder text.");
            }
        }
    }

    private static void ValidateSmtp(IConfiguration configuration, List<string> problems)
    {
        var section = configuration.GetSection(EmailOptions.SectionName);

        var host = section[nameof(EmailOptions.Host)];
        if (string.IsNullOrWhiteSpace(host))
        {
            problems.Add("Email__Smtp__Host is not set.");
        }
        else if (LooksLikePlaceholder(host) || Uri.CheckHostName(host.Trim()) == UriHostNameType.Unknown)
        {
            problems.Add("Email__Smtp__Host is not a valid host name.");
        }

        // Absent means the documented default (587 / StartTls); present must be valid.
        var port = section[nameof(EmailOptions.Port)];
        if (!string.IsNullOrWhiteSpace(port)
            && (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var portNumber) || portNumber is < 1 or > 65535))
        {
            problems.Add("Email__Smtp__Port must be a number between 1 and 65535.");
        }

        // A bare number would bind to the enum too; only the documented names are accepted.
        var tlsMode = section[nameof(EmailOptions.TlsMode)];
        if (!string.IsNullOrWhiteSpace(tlsMode)
            && !Enum.GetNames<EmailTlsMode>().Contains(tlsMode.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            problems.Add("Email__Smtp__TlsMode must be one of None, StartTls or SslOnConnect.");
        }

        var fromAddress = section[nameof(EmailOptions.FromAddress)];
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            problems.Add("Email__Smtp__FromAddress is not set.");
        }
        else if (!IsPlainEmailAddress(fromAddress))
        {
            problems.Add("Email__Smtp__FromAddress is not a valid email address.");
        }

        var replyTo = section[nameof(EmailOptions.ReplyToAddress)];
        if (!string.IsNullOrWhiteSpace(replyTo) && !IsPlainEmailAddress(replyTo))
        {
            problems.Add("Email__Smtp__ReplyToAddress is not a valid email address.");
        }

        // The sender identity (the responsible RH person) is required, like
        // every signature line: configuration problems fail at startup, while
        // an unreachable SMTP server never does (it is never contacted here).
        AddIfNotNull(problems, CheckDisplayText(section[nameof(EmailOptions.FromDisplayName)], "Email__Smtp__FromDisplayName"));

        var hasUsername = !string.IsNullOrWhiteSpace(section[nameof(EmailOptions.Username)]);
        var hasPassword = !string.IsNullOrWhiteSpace(section[nameof(EmailOptions.Password)]);
        if (hasUsername != hasPassword)
        {
            problems.Add("Email__Smtp__Username and Email__Smtp__Password must be set together, or both left empty.");
        }

        var signature = configuration.GetSection(EmailSignatureOptions.SectionName);
        AddIfNotNull(problems, CheckDisplayText(signature[nameof(EmailSignatureOptions.Name)], "Email__Signature__Name"));
        AddIfNotNull(problems, CheckDisplayText(signature[nameof(EmailSignatureOptions.Role)], "Email__Signature__Role"));
        AddIfNotNull(problems, CheckDisplayText(signature[nameof(EmailSignatureOptions.Company)], "Email__Signature__Company"));
    }

    /// <summary>
    /// A required, human-readable value shown to recipients (a name, a role,
    /// a company): set, not placeholder text, and free of control characters
    /// such as CR/LF. Any letters are fine — international names are valid.
    /// </summary>
    private static string? CheckDisplayText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"{name} is not set.";
        }

        if (value.Any(char.IsControl))
        {
            return $"{name} must be a single line without control characters.";
        }

        return LooksLikePlaceholder(value) ? $"{name} still holds placeholder text." : null;
    }

    private static void ValidatePublicBaseUrl(IConfiguration configuration, List<string> problems)
    {
        var value = configuration[$"{ApplicationLinkOptions.SectionName}:{nameof(ApplicationLinkOptions.PublicBaseUrl)}"];
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add("App__PublicBaseUrl is not set.");
            return;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || string.IsNullOrEmpty(uri.Host)
            || LooksLikePlaceholder(value))
        {
            problems.Add("App__PublicBaseUrl must be an absolute https:// URL of the real site.");
        }
    }

    /// <summary>
    /// The bootstrap itself validates completeness and password strength. This
    /// only stops the example file's values from creating the first administrator.
    /// </summary>
    private static void ValidateBootstrapAdmin(IConfiguration configuration, List<string> problems)
    {
        var section = configuration.GetSection(BootstrapAdminOptions.SectionName);
        if (!bool.TryParse(section[nameof(BootstrapAdminOptions.Enabled)], out var enabled) || !enabled)
        {
            return;
        }

        if (LooksLikePlaceholder(section[nameof(BootstrapAdminOptions.Email)]))
        {
            problems.Add("BootstrapAdmin__Email still holds placeholder text.");
        }

        if (LooksLikePlaceholder(section[nameof(BootstrapAdminOptions.Password)]))
        {
            problems.Add("BootstrapAdmin__Password still holds placeholder text.");
        }
    }

    private static bool IsPlainEmailAddress(string value)
    {
        var trimmed = value.Trim();
        return !LooksLikePlaceholder(trimmed)
            && !trimmed.Any(char.IsWhiteSpace)
            && MailboxAddress.TryParse(trimmed, out var mailbox)
            && string.Equals(mailbox.Address, trimmed, StringComparison.OrdinalIgnoreCase)
            && mailbox.Domain.Contains('.', StringComparison.Ordinal);
    }

    private static void AddIfNotNull(List<string> problems, string? problem)
    {
        if (problem is not null)
        {
            problems.Add(problem);
        }
    }
}
