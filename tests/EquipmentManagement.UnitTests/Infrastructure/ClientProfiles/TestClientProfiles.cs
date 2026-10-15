using EquipmentManagement.Infrastructure.ClientProfiles;

namespace EquipmentManagement.UnitTests.Infrastructure.ClientProfiles;

/// <summary>
/// Client profiles for tests. <see cref="Synthetic"/> is built in code with
/// recognizable fictional values, so generic document tests exercise the
/// profile mechanism without depending on any real client's wording.
/// <see cref="LoadFromRepository"/> reads a real profile directory
/// (clients/&lt;profile&gt;/) from this checkout.
/// </summary>
public static class TestClientProfiles
{
    public const string SupportContact = "suporte@example.test";
    public const string CarrierName = "Exemplo";

    // Deliberately not the PNG the document tests use as photos: PDFsharp
    // stores identical images once, which would hide the logo in image counts.
    private static readonly byte[] LogoPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static ClientProfile Synthetic { get; } = new(
        new ClientIdentity
        {
            Id = "synthetic-test",
            ProductName = "Synthetic Equipment",
            ShortName = "Synthetic",
            ProductWord = "Equipment",
            OrganizationName = "Synthetic Org",
            DataProtectionApplicationName = "Synthetic Equipment",
            MobileCarrierName = CarrierName,
            IsDemonstration = false,
        },
        new ResponsibilityTermContent
        {
            Logo = new ResponsibilityTermLogo { File = "document/logo.png", WidthCm = 3.6, Bytes = LogoPng },
            AccentColor = "#336699",
            Laptop = new DeviceTermContent
            {
                Indemnity = new DeviceIndemnity { Amount = 1200m, AmountInWords = "mil e duzentos euros" },
                Clauses =
                [
                    "O portátil é cedido pela organização sintética.",
                    "Em caso de perda, o utilizador compensa a organização em {indemnity}.",
                    $"Avarias são comunicadas para {SupportContact}.",
                ],
            },
            MobilePhone = new DeviceTermContent
            {
                Indemnity = new DeviceIndemnity { Amount = 180m, AmountInWords = "cento e oitenta euros" },
                Clauses =
                [
                    "O telemóvel é cedido pela organização sintética.",
                    "Em caso de perda, o utilizador compensa a organização em {indemnity}.",
                    $"Avarias são comunicadas para {SupportContact}.",
                ],
            },
        });

    public static ClientProfile LoadFromRepository(string profileId) =>
        ClientProfileLoader.LoadFromDirectory(ProfileDirectory(profileId));

    public static string ProfileDirectory(string profileId)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EquipmentManagement.slnx")))
            {
                return Path.Combine(directory.FullName, "clients", profileId);
            }
        }

        throw new InvalidOperationException("Repository root (EquipmentManagement.slnx) not found above the test output directory.");
    }
}
