using System.Globalization;

namespace EquipmentManagement.Infrastructure.ClientProfiles;

/// <summary>
/// The product's own wording rules around client-supplied values. Every
/// client gets the same rule; only the values differ.
/// </summary>
public static class ResponsibilityTermText
{
    /// <summary>The one placeholder a profile's legal clauses may contain.</summary>
    public const string IndemnityPlaceholder = "{indemnity}";

    /// <summary>pt-PT money as written in the terms: "." groups thousands, "," before cents.</summary>
    private static readonly NumberFormatInfo AmountFormat = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
    };

    /// <summary>A clause with its indemnity filled in, e.g. "… até 1.200,00€ (mil e duzentos euros) …".</summary>
    public static string RenderClause(string clause, DeviceIndemnity? indemnity) =>
        indemnity is null ? clause : clause.Replace(IndemnityPlaceholder, FormatIndemnity(indemnity), StringComparison.Ordinal);

    public static string FormatIndemnity(DeviceIndemnity indemnity) =>
        $"{indemnity.Amount.ToString("#,##0.00", AmountFormat)}€ ({indemnity.AmountInWords})";

    /// <summary>The mobile number's label in the UI, the term and the import template: "Cartão &lt;operator&gt;" when the company names its operator.</summary>
    public static string MobileNumberLabel(ClientIdentity identity) =>
        identity.MobileCarrierName is { } carrier ? $"Cartão {carrier}" : "N.º de telemóvel";

    /// <summary>The import template's mobile number column header.</summary>
    public static string MobileNumberImportHeader(ClientIdentity identity) =>
        identity.MobileCarrierName is { } carrier ? $"N.º de telemóvel / Cartão {carrier}" : "N.º de telemóvel";

    /// <summary>The Mobile Phone form's designation heading.</summary>
    public static string MobilePhoneDesignationHeading(ClientIdentity identity) =>
        identity.MobileCarrierName is { } carrier
            ? $"DESIGNAÇÃO DO TELEMÓVEL – CARTÃO {carrier.ToUpperInvariant()}"
            : "DESIGNAÇÃO DO TELEMÓVEL";
}
