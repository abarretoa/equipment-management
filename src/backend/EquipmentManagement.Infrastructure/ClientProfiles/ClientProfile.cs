namespace EquipmentManagement.Infrastructure.ClientProfiles;

/// <summary>
/// The client identity of this deployment (docs/WHITE_LABEL.md): everything
/// that differs between client deployments of the same product, loaded from
/// the <c>clients/&lt;profile&gt;/</c> directory selected at build time. One
/// deployment serves exactly one company, so there is exactly one profile per
/// process. It is never stored in the database and never edited by users.
/// Product rules never branch on <see cref="ClientIdentity.Id"/>: every
/// difference is a typed value below.
/// </summary>
public sealed record ClientProfile(ClientIdentity Identity, ResponsibilityTermContent ResponsibilityTerm);

/// <summary>Contents of <c>profile.json</c>, shared with the frontend build.</summary>
public sealed record ClientIdentity
{
    /// <summary>The profile directory name. Diagnostics only; never used to choose behaviour.</summary>
    public required string Id { get; init; }

    /// <summary>The product name shown to users (page title, product lockup).</summary>
    public required string ProductName { get; init; }

    /// <summary>The short brand name (the logo's accessible text).</summary>
    public required string ShortName { get; init; }

    /// <summary>The word set beside the logo in the product lockup.</summary>
    public required string ProductWord { get; init; }

    /// <summary>The company that runs this deployment, as written in user-facing copy (e.g. "funcionário interno da …").</summary>
    public required string OrganizationName { get; init; }

    /// <summary>
    /// ASP.NET Core Data Protection application name: isolates this
    /// deployment's cookie and CSRF keys. Changing it signs every user out, so
    /// it is set once per client and never derived from a display name.
    /// </summary>
    public required string DataProtectionApplicationName { get; init; }

    /// <summary>
    /// The mobile operator whose SIM cards this company issues, when its
    /// documents name one (the mobile number is then labelled "Cartão
    /// &lt;operator&gt;"); null for the generic "N.º de telemóvel" wording.
    /// </summary>
    public string? MobileCarrierName { get; init; }

    /// <summary>A demonstration profile: its document wording is illustrative and not legally reviewed.</summary>
    public required bool IsDemonstration { get; init; }
}

/// <summary>Contents of <c>responsibility-term.json</c> plus the logo it names: the client's Responsibility Term content.</summary>
public sealed record ResponsibilityTermContent
{
    /// <summary>The issuer's logo, top right of every Template V2 term; null for none.</summary>
    public ResponsibilityTermLogo? Logo { get; init; }

    /// <summary>Colour of the term's title and device-form headings, as #RRGGBB.</summary>
    public required string AccentColor { get; init; }

    /// <summary>A notice printed under the title of every term; null for none.</summary>
    public string? DemonstrationNotice { get; init; }

    /// <summary>The Laptop form ("Ficha de Empréstimo de Portátil").</summary>
    public required DeviceTermContent Laptop { get; init; }

    /// <summary>The Mobile Phone form ("Ficha de Empréstimo de Telemóvel").</summary>
    public required DeviceTermContent MobilePhone { get; init; }
}

public sealed record ResponsibilityTermLogo
{
    /// <summary>Path of the PNG or JPEG inside the profile directory.</summary>
    public required string File { get; init; }

    public required double WidthCm { get; init; }

    /// <summary>The image bytes, filled in by <see cref="ClientProfileLoader"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] Bytes { get; init; } = [];
}

/// <summary>A device form's legal clauses and the indemnity they may quote through the <c>{indemnity}</c> placeholder.</summary>
public sealed record DeviceTermContent
{
    public DeviceIndemnity? Indemnity { get; init; }

    public required IReadOnlyList<string> Clauses { get; init; }
}

/// <summary>
/// A client business rule: the minimum indemnity owed for a lost or seriously
/// damaged device. Rendered in pt-PT as "1.200,00€ (mil e duzentos euros)".
/// </summary>
public sealed record DeviceIndemnity
{
    public required decimal Amount { get; init; }

    public required string AmountInWords { get; init; }
}
