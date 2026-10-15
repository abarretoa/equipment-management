namespace EquipmentManagement.Infrastructure.Common;

/// <summary>
/// The one configured base URL for building links back into the application
/// from outbound emails (e.g. the Equipment Retirement Request notification's
/// "Analisar solicitação" CTA). No existing setting served this purpose — every
/// prior email either carried no link (Responsibility Term delivery) or a
/// standalone document. Deliberately just the origin (scheme+host[+port]),
/// never hardcoded to localhost or a specific deployment's domain — set per
/// environment via <c>App__PublicBaseUrl</c> (see .env.example).
/// </summary>
public sealed class ApplicationLinkOptions
{
    public const string SectionName = "App";

    public string? PublicBaseUrl { get; set; }

    /// <summary>Joins a relative, leading-slash frontend path onto the configured base URL. Returns null if unconfigured — callers must omit the CTA rather than link to nothing/localhost.</summary>
    public string? BuildUrl(string relativePath) =>
        string.IsNullOrWhiteSpace(PublicBaseUrl) ? null : $"{PublicBaseUrl.TrimEnd('/')}{relativePath}";
}
