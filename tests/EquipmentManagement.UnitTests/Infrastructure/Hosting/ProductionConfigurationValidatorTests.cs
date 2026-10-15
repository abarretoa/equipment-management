using EquipmentManagement.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;

namespace EquipmentManagement.UnitTests.Infrastructure.Hosting;

/// <summary>
/// The Production startup gate: a complete, real-looking configuration passes,
/// and each missing, malformed or placeholder value is named in the result.
/// </summary>
public sealed class ProductionConfigurationValidatorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"em-prodcfg-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidConfiguration_HasNoProblems_AndDoesNotThrow()
    {
        var configuration = Build();

        Assert.Empty(ProductionConfigurationValidator.Validate(configuration));
        ProductionConfigurationValidator.ThrowIfInvalid(configuration);
        Assert.True(Directory.Exists(Path.Combine(_tempRoot, "storage")));
        Assert.True(Directory.Exists(Path.Combine(_tempRoot, "keys")));
    }

    [Fact]
    public void ThrowIfInvalid_ListsEveryProblem_AndNeverEchoesConfiguredValues()
    {
        var configuration = Build(new()
        {
            ["Documents:Issuer:LegalName"] = "Example Consulting, Lda. (REPLACE — not a real company)",
            ["Email:Smtp:Username"] = null,
            ["App:PublicBaseUrl"] = "http://equipment.example.test",
        });

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.ThrowIfInvalid(configuration));

        Assert.Contains("Documents__Issuer__LegalName", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Email__Smtp__Username", ex.Message, StringComparison.Ordinal);
        Assert.Contains("App__PublicBaseUrl", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Example Consulting", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("smtp-secret-value", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("example.test", ex.Message, StringComparison.Ordinal);
    }

    // --- Document issuer ---------------------------------------------------

    [Theory]
    [InlineData("Documents:Issuer:LegalName", "Documents__Issuer__LegalName")]
    [InlineData("Documents:Issuer:Address", "Documents__Issuer__Address")]
    [InlineData("Documents:Issuer:TaxId", "Documents__Issuer__TaxId")]
    public void Issuer_MissingOrWhitespace_IsRejected(string key, string settingName)
    {
        AssertProblem(Build(new() { [key] = null }), $"{settingName} is not set.");
        AssertProblem(Build(new() { [key] = "   " }), $"{settingName} is not set.");
    }

    [Theory]
    [InlineData("Documents:Issuer:LegalName", "Example Consulting, Lda. (REPLACE — not a real company)")]
    [InlineData("Documents:Issuer:LegalName", "Example Consulting, Lda.")]
    [InlineData("Documents:Issuer:LegalName", "changeme")]
    [InlineData("Documents:Issuer:Address", "Rua Exemplo 123, 1000-001 Lisboa (REPLACE — not a real address)")]
    [InlineData("Documents:Issuer:Address", "ChangeMe")]
    [InlineData("Documents:Issuer:Address", "contact@example.invalid")]
    [InlineData("Documents:Issuer:TaxId", "000000000")]
    [InlineData("Documents:Issuer:TaxId", "REPLACE")]
    public void Issuer_Placeholder_IsRejected(string key, string value)
    {
        var settingName = key.Replace(":", "__", StringComparison.Ordinal);

        AssertProblem(Build(new() { [key] = value }), $"{settingName} still holds placeholder text.");
    }

    [Fact]
    public void Issuer_OrdinaryWordContainingReplace_IsAccepted()
    {
        Assert.Empty(ProductionConfigurationValidator.Validate(Build(new() { ["Documents:Issuer:LegalName"] = "Replacement Parts, Lda." })));
    }

    // --- SMTP --------------------------------------------------------------

    [Theory]
    [InlineData("Email:Smtp:Host", null, "Email__Smtp__Host is not set.")]
    [InlineData("Email:Smtp:Host", "  ", "Email__Smtp__Host is not set.")]
    [InlineData("Email:Smtp:Host", "smtp host with spaces", "Email__Smtp__Host is not a valid host name.")]
    [InlineData("Email:Smtp:Host", "smtp.example.invalid", "Email__Smtp__Host is not a valid host name.")]
    [InlineData("Email:Smtp:Port", "0", "Email__Smtp__Port must be a number between 1 and 65535.")]
    [InlineData("Email:Smtp:Port", "70000", "Email__Smtp__Port must be a number between 1 and 65535.")]
    [InlineData("Email:Smtp:Port", "smtp", "Email__Smtp__Port must be a number between 1 and 65535.")]
    [InlineData("Email:Smtp:TlsMode", "Tls", "Email__Smtp__TlsMode must be one of None, StartTls or SslOnConnect.")]
    [InlineData("Email:Smtp:TlsMode", "1", "Email__Smtp__TlsMode must be one of None, StartTls or SslOnConnect.")]
    [InlineData("Email:Smtp:FromAddress", null, "Email__Smtp__FromAddress is not set.")]
    [InlineData("Email:Smtp:FromAddress", "not-an-address", "Email__Smtp__FromAddress is not a valid email address.")]
    [InlineData("Email:Smtp:FromAddress", "equipamentos@example-consulting.invalid (REPLACE — not a real address)", "Email__Smtp__FromAddress is not a valid email address.")]
    [InlineData("Email:Smtp:FromAddress", "equipamentos@example-consulting.invalid", "Email__Smtp__FromAddress is not a valid email address.")]
    [InlineData("Email:Smtp:ReplyToAddress", "nobody", "Email__Smtp__ReplyToAddress is not a valid email address.")]
    [InlineData("Email:Smtp:FromDisplayName", "Example Consulting — Gestão de Equipamentos (REPLACE)", "Email__Smtp__FromDisplayName still holds placeholder text.")]
    [InlineData("Email:Smtp:FromDisplayName", null, "Email__Smtp__FromDisplayName is not set.")]
    [InlineData("Email:Smtp:FromDisplayName", "Maria\r\nBcc: intruso@example.test", "Email__Smtp__FromDisplayName must be a single line without control characters.")]
    [InlineData("Email:Smtp:ReplyToAddress", "rh@example.test\r\nBcc: intruso@example.test", "Email__Smtp__ReplyToAddress is not a valid email address.")]
    [InlineData("Email:Signature:Name", null, "Email__Signature__Name is not set.")]
    [InlineData("Email:Signature:Name", "Maria\nFictícia", "Email__Signature__Name must be a single line without control characters.")]
    [InlineData("Email:Signature:Role", " ", "Email__Signature__Role is not set.")]
    [InlineData("Email:Signature:Company", null, "Email__Signature__Company is not set.")]
    [InlineData("Email:Signature:Company", "Example Consulting", "Email__Signature__Company still holds placeholder text.")]
    public void Smtp_InvalidValue_IsRejected(string key, string? value, string expectedProblem)
    {
        AssertProblem(Build(new() { [key] = value }), expectedProblem);
    }

    [Theory]
    [InlineData("mailer", null)]
    [InlineData(null, "smtp-secret-value")]
    public void Smtp_UsernameWithoutPassword_OrPasswordWithoutUsername_IsRejected(string? username, string? password)
    {
        var configuration = Build(new() { ["Email:Smtp:Username"] = username, ["Email:Smtp:Password"] = password });

        AssertProblem(configuration, "Email__Smtp__Username and Email__Smtp__Password must be set together, or both left empty.");
    }

    [Fact]
    public void Smtp_ValidVariants_AreAccepted()
    {
        // Anonymous relay, default port and TLS mode.
        Assert.Empty(ProductionConfigurationValidator.Validate(Build(new()
        {
            ["Email:Smtp:Port"] = null,
            ["Email:Smtp:TlsMode"] = null,
            ["Email:Smtp:Username"] = null,
            ["Email:Smtp:Password"] = null,
        })));

        // Implicit TLS with credentials and a reply-to address; a single-label internal relay host.
        Assert.Empty(ProductionConfigurationValidator.Validate(Build(new()
        {
            ["Email:Smtp:Host"] = "mailrelay",
            ["Email:Smtp:Port"] = "465",
            ["Email:Smtp:TlsMode"] = "sslonconnect",
            ["Email:Smtp:ReplyToAddress"] = "respostas@example.test",
        })));
    }

    // --- Public URL --------------------------------------------------------

    [Fact]
    public void PublicBaseUrl_Missing_IsRejected()
    {
        AssertProblem(Build(new() { ["App:PublicBaseUrl"] = null }), "App__PublicBaseUrl is not set.");
        AssertProblem(Build(new() { ["App:PublicBaseUrl"] = "" }), "App__PublicBaseUrl is not set.");
    }

    [Theory]
    [InlineData("http://equipment.example.test")]
    [InlineData("equipment.example.test")]
    [InlineData("/solicitacoes-abate")]
    [InlineData("https://app.example.invalid")]
    public void PublicBaseUrl_NotAbsoluteHttps_IsRejected(string value)
    {
        AssertProblem(Build(new() { ["App:PublicBaseUrl"] = value }), "App__PublicBaseUrl must be an absolute https:// URL of the real site.");
    }

    [Fact]
    public void PublicBaseUrl_AbsoluteHttps_IsAccepted()
    {
        Assert.Empty(ProductionConfigurationValidator.Validate(Build(new() { ["App:PublicBaseUrl"] = "https://equipment.example.test/" })));
    }

    // --- Private storage / Data Protection ---------------------------------

    [Theory]
    [InlineData("PrivateStorage:RootPath", "PrivateStorage__RootPath")]
    [InlineData("DataProtection:KeyRingPath", "DataProtection__KeyRingPath")]
    public void Directory_Missing_IsRejected(string key, string settingName)
    {
        AssertProblem(Build(new() { [key] = null }), $"{settingName} is not set.");
    }

    [Theory]
    [InlineData("PrivateStorage:RootPath", "PrivateStorage__RootPath")]
    [InlineData("DataProtection:KeyRingPath", "DataProtection__KeyRingPath")]
    public void Directory_RelativePath_IsRejected(string key, string settingName)
    {
        AssertProblem(Build(new() { [key] = "private-storage" }), $"{settingName} must be an absolute path.");
        AssertProblem(Build(new() { [key] = Path.Combine("data", "keys") }), $"{settingName} must be an absolute path.");
    }

    [Theory]
    [InlineData("PrivateStorage:RootPath", "PrivateStorage__RootPath")]
    [InlineData("DataProtection:KeyRingPath", "DataProtection__KeyRingPath")]
    public void Directory_ThatCannotBeCreatedOrWritten_IsRejected(string key, string settingName)
    {
        // A path below a regular file can never become a directory, on any OS
        // and for any user — unlike a permission-based fixture, which a root
        // or administrator test run would simply write through.
        Directory.CreateDirectory(_tempRoot);
        var blockingFile = Path.Combine(_tempRoot, "not-a-directory");
        File.WriteAllText(blockingFile, "x");

        var problems = ProductionConfigurationValidator.Validate(Build(new() { [key] = Path.Combine(blockingFile, "child") }));

        Assert.Contains(problems, p => p.StartsWith($"{settingName} is not writable by the application user", StringComparison.Ordinal));
    }

    [Fact]
    public void EmailIdentity_AcceptsInternationalNames_AndAnAbsentReplyTo()
    {
        Assert.Empty(ProductionConfigurationValidator.Validate(Build(new()
        {
            ["Email:Smtp:FromDisplayName"] = "Zoë Ångström-Núñez Conceição",
            ["Email:Smtp:ReplyToAddress"] = null,
            ["Email:Signature:Name"] = "Zoë Ångström-Núñez Conceição",
            ["Email:Signature:Role"] = "Responsável de Recursos Humanos — Gestão de Pessoas",
            ["Email:Signature:Company"] = "Acme",
        })));
    }

    // --- Bootstrap administrator -------------------------------------------

    [Fact]
    public void BootstrapAdmin_ExampleValues_AreRejectedOnlyWhenEnabled()
    {
        var exampleValues = new Dictionary<string, string?>
        {
            ["BootstrapAdmin:Email"] = "admin.bootstrap@example.invalid",
            ["BootstrapAdmin:Password"] = "Replace-With-A-Real-Secret-20+chars",
        };

        Assert.Empty(ProductionConfigurationValidator.Validate(Build(new(exampleValues) { ["BootstrapAdmin:Enabled"] = "false" })));

        var problems = ProductionConfigurationValidator.Validate(Build(new(exampleValues) { ["BootstrapAdmin:Enabled"] = "true" }));
        Assert.Contains("BootstrapAdmin__Email still holds placeholder text.", problems);
        Assert.Contains("BootstrapAdmin__Password still holds placeholder text.", problems);
    }

    private static void AssertProblem(IConfiguration configuration, string expectedProblem)
    {
        var problem = Assert.Single(ProductionConfigurationValidator.Validate(configuration));
        Assert.Equal(expectedProblem, problem);
    }

    private IConfiguration Build(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Documents:Issuer:LegalName"] = "Acme Test Fixture, Lda.",
            ["Documents:Issuer:Address"] = "Avenida de Teste 10, 1000-100 Lisboa",
            ["Documents:Issuer:TaxId"] = "500100200",
            ["Email:Smtp:Host"] = "smtp.example.test",
            ["Email:Smtp:Port"] = "587",
            ["Email:Smtp:TlsMode"] = "StartTls",
            ["Email:Smtp:Username"] = "mailer",
            ["Email:Smtp:Password"] = "smtp-secret-value",
            ["Email:Smtp:FromAddress"] = "equipamentos@example.test",
            ["Email:Smtp:FromDisplayName"] = "Maria Fictícia",
            ["Email:Signature:Name"] = "Maria Fictícia",
            ["Email:Signature:Role"] = "Técnica de Recursos Humanos",
            ["Email:Signature:Company"] = "Acme",
            ["App:PublicBaseUrl"] = "https://equipment.example.test",
            ["PrivateStorage:RootPath"] = Path.Combine(_tempRoot, "storage"),
            ["DataProtection:KeyRingPath"] = Path.Combine(_tempRoot, "keys"),
        };

        foreach (var (key, value) in overrides ?? [])
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
