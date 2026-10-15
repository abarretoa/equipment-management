namespace EquipmentManagement.Domain.Equipments;

/// <summary>Pure validation for <see cref="MobilePhoneSpecification"/> — mirrors <see cref="LaptopSpecificationRules"/>. Only IMEI is required; carrier/SIM details are informational.</summary>
public static class MobilePhoneSpecificationRules
{
    /// <summary>An IMEI is exactly this many decimal digits once separators are removed.</summary>
    public const int ImeiDigitCount = 15;

    /// <summary>
    /// An IMEI is present only if something remains once separators are
    /// removed: "-" or " - - " is a missing IMEI, not a malformed one.
    /// Whether what remains is a well-formed IMEI is <see cref="IsValidImei"/>.
    /// </summary>
    public static bool IsComplete(string? imei) => NormalizeImei(imei) is not null;

    /// <summary>
    /// The structural IMEI format, required of every persisted IMEI: after
    /// <see cref="NormalizeImei"/>, exactly <see cref="ImeiDigitCount"/>
    /// characters, every one 0-9. Deliberately no Luhn/check-digit
    /// validation — that stricter policy has not been decided.
    /// </summary>
    public static bool IsValidImei(string? imei) =>
        NormalizeImei(imei) is { Length: ImeiDigitCount } normalized && normalized.All(char.IsAsciiDigit);

    /// <summary>
    /// The canonical form of an IMEI — the product-wide uniqueness key (one
    /// IMEI identifies exactly one mobile phone): whitespace and hyphens, the
    /// harmless visual separators an IMEI is written with, are removed (and
    /// letters upper-cased, which only matters for input <see cref="IsValidImei"/>
    /// then rejects). For a valid IMEI this is exactly its 15 digits. Same
    /// shape as <see cref="EquipmentRules.NormalizeSerialNumber"/>; null/blank
    /// (or separators only) is null.
    /// </summary>
    public static string? NormalizeImei(string? imei)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            return null;
        }

        var normalized = new string([.. imei.Where(c => !char.IsWhiteSpace(c) && c != '-')]).ToUpperInvariant();
        return normalized.Length == 0 ? null : normalized;
    }
}
