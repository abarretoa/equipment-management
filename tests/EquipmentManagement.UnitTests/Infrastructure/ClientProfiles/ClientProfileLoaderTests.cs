using System.Reflection;
using EquipmentManagement.Infrastructure.ClientProfiles;

namespace EquipmentManagement.UnitTests.Infrastructure.ClientProfiles;

/// <summary>
/// Loading and validating client profiles (docs/WHITE_LABEL.md). Uses the
/// profile embedded in this build and throw-away profile directories built
/// per test.
/// </summary>
public sealed class ClientProfileLoaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"client-profile-test-{Guid.NewGuid():N}");

    public ClientProfileLoaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void EmbeddedProfile_IsValid_AndIsTheProfileThisBuildRecorded()
    {
        var profile = ClientProfileLoader.Embedded;
        var recorded = typeof(ClientProfileLoader).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == ClientProfileLoader.AssemblyMetadataKey).Value;

        Assert.Equal(recorded, profile.Identity.Id);
        Assert.Empty(ClientProfileLoader.Validate(profile));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("synthetic-test")]
    [InlineData(" synthetic-test ")]
    public void ExpectedProfile_AbsentOrMatching_IsAccepted(string? expected)
    {
        ClientProfileLoader.EnsureExpected(TestClientProfiles.Synthetic, expected);
    }

    [Fact]
    public void ExpectedProfile_ThatDiffersFromTheBuild_StopsStartup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ClientProfileLoader.EnsureExpected(TestClientProfiles.Synthetic, "other-client"));

        Assert.Contains("embeds client profile 'synthetic-test'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'other-client'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidProfileDirectory_LoadsItsLogoBytes()
    {
        WriteProfile(logo: """{ "file": "document/logo.png", "widthCm": 3 }""");
        Directory.CreateDirectory(Path.Combine(_directory, "document"));
        File.WriteAllBytes(Path.Combine(_directory, "document", "logo.png"), OnePixelPng);

        var profile = ClientProfileLoader.LoadFromDirectory(_directory);

        Assert.Equal(OnePixelPng, profile.ResponsibilityTerm.Logo!.Bytes);
        Assert.Equal(3, profile.ResponsibilityTerm.Logo.WidthCm);
    }

    [Theory]
    [InlineData("""["Clause with {amount}."]""", "unknown placeholder {amount}")]
    [InlineData("""["Clause without the amount."]""", "indemnity is set but no clause quotes {indemnity}")]
    [InlineData("""[]""", "at least one clause")]
    [InlineData("""["  "]""", "no empty clause")]
    public void InvalidClauses_AreRejected_WithTheReason(string laptopClauses, string expectedProblem)
    {
        WriteProfile(laptopClauses: laptopClauses);

        var ex = Assert.Throws<InvalidOperationException>(() => ClientProfileLoader.LoadFromDirectory(_directory));

        Assert.Contains(expectedProblem, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClauseQuotingAnIndemnity_WithoutOne_IsRejected()
    {
        WriteProfile(laptopIndemnity: "null");

        var ex = Assert.Throws<InvalidOperationException>(() => ClientProfileLoader.LoadFromDirectory(_directory));

        Assert.Contains("laptop.indemnity is not set", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"accentColor\": \"orange\"", "accentColor must be a #RRGGBB colour")]
    [InlineData("\"logo\": { \"file\": \"../outside.png\", \"widthCm\": 3 }", "logo.file must be a relative path")]
    [InlineData("\"logo\": { \"file\": \"document/missing.png\", \"widthCm\": 3 }", "does not exist")]
    public void InvalidTermSettings_AreRejected(string setting, string expectedProblem)
    {
        WriteProfile(extraTermSetting: setting);

        var ex = Assert.Throws<InvalidOperationException>(() => ClientProfileLoader.LoadFromDirectory(_directory));

        Assert.Contains(expectedProblem, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LogoThatIsNotAnImage_IsRejected()
    {
        WriteProfile(logo: """{ "file": "document/logo.png", "widthCm": 3 }""");
        Directory.CreateDirectory(Path.Combine(_directory, "document"));
        File.WriteAllText(Path.Combine(_directory, "document", "logo.png"), "not an image");

        var ex = Assert.Throws<InvalidOperationException>(() => ClientProfileLoader.LoadFromDirectory(_directory));

        Assert.Contains("PNG or JPEG", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DemonstrationProfile_WithoutANotice_IsRejected()
    {
        WriteProfile(isDemonstration: true, notice: "null");

        var ex = Assert.Throws<InvalidOperationException>(() => ClientProfileLoader.LoadFromDirectory(_directory));

        Assert.Contains("demonstration profile must print a demonstrationNotice", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Acme", "Bad Id", "id must be lower-case")]
    [InlineData("Acme\nSecond line", "acme", "organizationName must be one non-empty line")]
    public void InvalidIdentity_IsRejected(string organizationName, string id, string expectedProblem)
    {
        WriteProfile(organizationName: organizationName, id: id);

        var ex = Assert.Throws<InvalidOperationException>(() => ClientProfileLoader.LoadFromDirectory(_directory));

        Assert.Contains(expectedProblem, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MisspelledOrUnknownSetting_IsRejected_RatherThanIgnored()
    {
        WriteProfile(extraTermSetting: "\"accentColour\": \"#000000\"");

        var ex = Assert.Throws<InvalidOperationException>(() => ClientProfileLoader.LoadFromDirectory(_directory));

        Assert.Contains("responsibility-term.json could not be read", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingProfileFile_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ClientProfileLoader.LoadFromDirectory(_directory));

        Assert.Contains("profile.json is missing", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1200, "mil e duzentos euros", "1.200,00€ (mil e duzentos euros)")]
    [InlineData(12500.5, "doze mil e quinhentos euros e cinquenta cêntimos", "12.500,50€ (doze mil e quinhentos euros e cinquenta cêntimos)")]
    [InlineData(180, "cento e oitenta euros", "180,00€ (cento e oitenta euros)")]
    public void Indemnity_IsWrittenInPtPt(double amount, string words, string expected)
    {
        var indemnity = new DeviceIndemnity { Amount = (decimal)amount, AmountInWords = words };

        Assert.Equal(expected, ResponsibilityTermText.FormatIndemnity(indemnity));
        Assert.Equal($"Valor: {expected}.", ResponsibilityTermText.RenderClause("Valor: {indemnity}.", indemnity));
    }

    [Fact]
    public void MobileNumberWording_NamesTheOperator_OnlyWhenTheProfileHasOne()
    {
        var withOperator = TestClientProfiles.Synthetic.Identity;
        var withoutOperator = withOperator with { MobileCarrierName = null };

        Assert.Equal("Cartão Exemplo", ResponsibilityTermText.MobileNumberLabel(withOperator));
        Assert.Equal("N.º de telemóvel / Cartão Exemplo", ResponsibilityTermText.MobileNumberImportHeader(withOperator));
        Assert.Equal("DESIGNAÇÃO DO TELEMÓVEL – CARTÃO EXEMPLO", ResponsibilityTermText.MobilePhoneDesignationHeading(withOperator));
        Assert.Equal("N.º de telemóvel", ResponsibilityTermText.MobileNumberLabel(withoutOperator));
        Assert.Equal("N.º de telemóvel", ResponsibilityTermText.MobileNumberImportHeader(withoutOperator));
        Assert.Equal("DESIGNAÇÃO DO TELEMÓVEL", ResponsibilityTermText.MobilePhoneDesignationHeading(withoutOperator));
    }

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private void WriteProfile(
        string id = "acme",
        string organizationName = "Acme",
        bool isDemonstration = false,
        string notice = "null",
        string logo = "null",
        string laptopClauses = """["Pays {indemnity}."]""",
        string laptopIndemnity = """{ "amount": 100, "amountInWords": "cem euros" }""",
        string? extraTermSetting = null)
    {
        File.WriteAllText(Path.Combine(_directory, ClientProfileLoader.ProfileFileName), $$"""
            {
              "id": {{System.Text.Json.JsonSerializer.Serialize(id)}},
              "productName": "Acme Equipment",
              "shortName": "Acme",
              "productWord": "Equipment",
              "organizationName": {{System.Text.Json.JsonSerializer.Serialize(organizationName)}},
              "dataProtectionApplicationName": "Acme Equipment",
              "mobileCarrierName": null,
              "isDemonstration": {{(isDemonstration ? "true" : "false")}}
            }
            """);

        var accent = extraTermSetting is not null && extraTermSetting.StartsWith("\"accentColor\"", StringComparison.Ordinal)
            ? extraTermSetting
            : "\"accentColor\": \"#123456\"";
        var logoSetting = extraTermSetting is not null && extraTermSetting.StartsWith("\"logo\"", StringComparison.Ordinal)
            ? extraTermSetting
            : $"\"logo\": {logo}";
        var extra = extraTermSetting is not null && !extraTermSetting.StartsWith("\"accentColor\"", StringComparison.Ordinal)
            && !extraTermSetting.StartsWith("\"logo\"", StringComparison.Ordinal)
            ? extraTermSetting + ","
            : string.Empty;

        File.WriteAllText(Path.Combine(_directory, ClientProfileLoader.ResponsibilityTermFileName), $$"""
            {
              {{extra}}
              {{logoSetting}},
              {{accent}},
              "demonstrationNotice": {{notice}},
              "laptop": { "indemnity": {{laptopIndemnity}}, "clauses": {{laptopClauses}} },
              "mobilePhone": { "clauses": ["Phone clause."] }
            }
            """);
    }
}
