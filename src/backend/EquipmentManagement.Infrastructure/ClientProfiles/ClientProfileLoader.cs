using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EquipmentManagement.Domain.Files;

namespace EquipmentManagement.Infrastructure.ClientProfiles;

/// <summary>
/// Reads and validates a client profile (docs/WHITE_LABEL.md). The running
/// application uses <see cref="Embedded"/>, the one profile compiled into this
/// assembly; tests may also load a profile directory directly.
/// </summary>
public static partial class ClientProfileLoader
{
    public const string ProfileFileName = "profile.json";
    public const string ResponsibilityTermFileName = "responsibility-term.json";

    /// <summary>Assembly metadata key holding the profile name the build embedded (see the Infrastructure .csproj).</summary>
    public const string AssemblyMetadataKey = "ClientProfile";

    /// <summary>Optional configuration key: when set, startup refuses any other embedded profile.</summary>
    public const string ExpectedProfileKey = "ClientProfile:Expected";

    private const string ResourcePrefix = "ClientProfile/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly Lazy<ClientProfile> EmbeddedProfile =
        new(() => LoadEmbedded(typeof(ClientProfileLoader).Assembly));

    /// <summary>
    /// The profile embedded in this build. It is fixed for the life of the
    /// process, like any other compiled-in resource, so static definitions
    /// (e.g. an import column header) may read it directly.
    /// </summary>
    public static ClientProfile Embedded => EmbeddedProfile.Value;

    public static ClientProfile LoadEmbedded(Assembly assembly)
    {
        // MSBuild writes the recursive part of a resource name with the
        // build machine's path separator.
        var resources = assembly.GetManifestResourceNames()
            .Select(name => (Name: name, Path: name.Replace('\\', '/')))
            .Where(r => r.Path.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToDictionary(r => r.Path[ResourcePrefix.Length..], r => r.Name, StringComparer.Ordinal);

        var embeddedId = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == AssemblyMetadataKey)?.Value;

        var profile = Load(
            path => resources.TryGetValue(path, out var resourceName) ? ReadResource(assembly, resourceName) : null,
            $"embedded client profile '{embeddedId}'");

        if (embeddedId is not null && !string.Equals(profile.Identity.Id, embeddedId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The embedded client profile declares id '{profile.Identity.Id}' but was built as '{embeddedId}'.");
        }

        return profile;
    }

    /// <summary>
    /// Refuses to run a build whose embedded profile is not the one the
    /// deployment expects (<see cref="ExpectedProfileKey"/>, set from the
    /// deployment's CLIENT_PROFILE). No expectation configured: no check.
    /// </summary>
    public static void EnsureExpected(ClientProfile profile, string? expectedProfileId)
    {
        if (!string.IsNullOrWhiteSpace(expectedProfileId)
            && !string.Equals(expectedProfileId.Trim(), profile.Identity.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"This build embeds client profile '{profile.Identity.Id}', but ClientProfile__Expected is '{expectedProfileId.Trim()}'. " +
                "Rebuild the images with the expected CLIENT_PROFILE.");
        }
    }

    public static ClientProfile LoadFromDirectory(string directory) =>
        Load(
            path =>
            {
                var fullPath = Path.Combine(directory, path);
                return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
            },
            $"client profile directory '{directory}'");

    private static ClientProfile Load(Func<string, byte[]?> readFile, string source)
    {
        var identity = Deserialize<ClientIdentity>(readFile, ProfileFileName, source);
        var term = Deserialize<ResponsibilityTermContent>(readFile, ResponsibilityTermFileName, source);

        var problems = new List<string>();
        if (term.Logo is { } logo)
        {
            if (string.IsNullOrWhiteSpace(logo.File) || logo.File.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(logo.File))
            {
                problems.Add("responsibility-term.json: logo.file must be a relative path inside the profile directory.");
            }
            else if (readFile(logo.File.Replace('\\', '/')) is { } bytes)
            {
                term = term with { Logo = logo with { Bytes = bytes } };
            }
            else
            {
                problems.Add($"responsibility-term.json: logo file '{logo.File}' does not exist.");
            }
        }

        var profile = new ClientProfile(identity, term);
        problems.AddRange(Validate(profile));
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid {source}:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => " - " + p)));
        }

        return profile;
    }

    /// <summary>Every problem in the profile, worded for the person editing its files; empty when it is valid.</summary>
    public static IReadOnlyList<string> Validate(ClientProfile profile)
    {
        var problems = new List<string>();
        var identity = profile.Identity;

        if (!ProfileIdPattern().IsMatch(identity.Id))
        {
            problems.Add("profile.json: id must be lower-case letters, digits and hyphens.");
        }

        foreach (var (name, value) in new[]
        {
            ("productName", identity.ProductName),
            ("shortName", identity.ShortName),
            ("productWord", identity.ProductWord),
            ("organizationName", identity.OrganizationName),
            ("dataProtectionApplicationName", identity.DataProtectionApplicationName),
        })
        {
            if (!IsSingleLineText(value))
            {
                problems.Add($"profile.json: {name} must be one non-empty line.");
            }
        }

        if (identity.MobileCarrierName is not null && !IsSingleLineText(identity.MobileCarrierName))
        {
            problems.Add("profile.json: mobileCarrierName must be null or one non-empty line.");
        }

        var term = profile.ResponsibilityTerm;
        if (!AccentColorPattern().IsMatch(term.AccentColor))
        {
            problems.Add("responsibility-term.json: accentColor must be a #RRGGBB colour.");
        }

        if (term.Logo is { } logo)
        {
            if (logo.Bytes.Length > 0 && ImageSignatureDetector.Detect(logo.Bytes) is not (ImageContentType.Png or ImageContentType.Jpeg))
            {
                problems.Add("responsibility-term.json: the logo must be a PNG or JPEG image.");
            }

            if (logo.WidthCm is <= 0 or > 17)
            {
                problems.Add("responsibility-term.json: logo.widthCm must be greater than 0 and at most 17 (the page's content width).");
            }
        }

        if (term.DemonstrationNotice is not null && string.IsNullOrWhiteSpace(term.DemonstrationNotice))
        {
            problems.Add("responsibility-term.json: demonstrationNotice must be null or non-empty text.");
        }

        if (identity.IsDemonstration && term.DemonstrationNotice is null)
        {
            problems.Add("responsibility-term.json: a demonstration profile must print a demonstrationNotice on its terms.");
        }

        ValidateDevice("laptop", term.Laptop, problems);
        ValidateDevice("mobilePhone", term.MobilePhone, problems);
        return problems;
    }

    private static void ValidateDevice(string name, DeviceTermContent device, List<string> problems)
    {
        if (device.Clauses.Count == 0 || device.Clauses.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add($"responsibility-term.json: {name}.clauses must contain at least one clause and no empty clause.");
        }

        var placeholders = device.Clauses.SelectMany(c => PlaceholderPattern().Matches(c)).Select(m => m.Value).ToList();
        foreach (var unknown in placeholders.Where(p => p != ResponsibilityTermText.IndemnityPlaceholder).Distinct())
        {
            problems.Add($"responsibility-term.json: {name}.clauses use the unknown placeholder {unknown}; only {ResponsibilityTermText.IndemnityPlaceholder} exists.");
        }

        var usesIndemnity = placeholders.Contains(ResponsibilityTermText.IndemnityPlaceholder);
        if (usesIndemnity && device.Indemnity is null)
        {
            problems.Add($"responsibility-term.json: {name}.clauses quote {ResponsibilityTermText.IndemnityPlaceholder} but {name}.indemnity is not set.");
        }
        else if (!usesIndemnity && device.Indemnity is not null)
        {
            problems.Add($"responsibility-term.json: {name}.indemnity is set but no clause quotes {ResponsibilityTermText.IndemnityPlaceholder}.");
        }

        if (device.Indemnity is { } indemnity && (indemnity.Amount <= 0 || !IsSingleLineText(indemnity.AmountInWords)))
        {
            problems.Add($"responsibility-term.json: {name}.indemnity needs an amount above 0 and the amount in words.");
        }
    }

    private static T Deserialize<T>(Func<string, byte[]?> readFile, string fileName, string source)
    {
        var bytes = readFile(fileName)
            ?? throw new InvalidOperationException($"Invalid {source}: {fileName} is missing.");
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new InvalidOperationException($"Invalid {source}: {fileName} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid {source}: {fileName} could not be read ({ex.Message}).", ex);
        }
    }

    private static byte[] ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool IsSingleLineText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.IndexOfAny(['\r', '\n']) < 0;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex ProfileIdPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex AccentColorPattern();

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex PlaceholderPattern();
}
