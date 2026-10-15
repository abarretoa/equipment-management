namespace EquipmentManagement.Infrastructure.Email;

/// <summary>
/// The corporate signature appended to operational emails, bound from
/// "Email:Signature". Configuration only: no person's identity is ever in
/// source. Typically the responsible HR person's name, role and the
/// company, matching the sender identity in <see cref="EmailOptions"/>.
/// Required in Production (see <c>ProductionConfigurationValidator</c>); when
/// unset elsewhere the signature is simply omitted.
/// </summary>
public sealed class EmailSignatureOptions
{
    public const string SectionName = "Email:Signature";

    public string? Name { get; set; }

    public string? Role { get; set; }

    public string? Company { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Role) && !string.IsNullOrWhiteSpace(Company);
}

/// <summary>The one renderer of the signature block (plain text, like every email body in this application).</summary>
public static class EmailSignature
{
    /// <summary>
    /// The body followed by a blank line and the configured identity, one
    /// value per line, under the template's own closing line (e.g.
    /// "Obrigada!") — no extra wording is invented. The body is returned
    /// unchanged when no signature is configured.
    /// </summary>
    public static string AppendTo(string body, EmailSignatureOptions signature)
    {
        if (!signature.IsConfigured)
        {
            return body;
        }

        return $"{body.TrimEnd()}\n\n{signature.Name!.Trim()}\n{signature.Role!.Trim()}\n{signature.Company!.Trim()}";
    }
}
