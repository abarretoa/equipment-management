using EquipmentManagement.Infrastructure;
using EquipmentManagement.Infrastructure.ClientProfiles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.UnitTests.Infrastructure.Hosting;

/// <summary>
/// The composition root's Data Protection wiring: Production refuses to start
/// without a usable key-ring path, and a configured path really holds the keys,
/// so a brand-new process (a recreated container) can read what the previous
/// one protected.
/// </summary>
public sealed class DataProtectionKeyRingTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"em-keyring-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Production_WithoutKeyRingPath_RefusesToStart()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BuildProvider("Production", keyRingPath: null));

        Assert.Contains("DataProtection__KeyRingPath is not set.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_WithRelativeKeyRingPath_RefusesToStart()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BuildProvider("Production", keyRingPath: "keys"));

        Assert.Contains("DataProtection__KeyRingPath must be an absolute path.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_WithUnusableKeyRingPath_RefusesToStart()
    {
        Directory.CreateDirectory(_tempRoot);
        var blockingFile = Path.Combine(_tempRoot, "not-a-directory");
        File.WriteAllText(blockingFile, "x");

        var ex = Assert.Throws<InvalidOperationException>(() => BuildProvider("Production", Path.Combine(blockingFile, "keys")));

        Assert.Contains("DataProtection__KeyRingPath is not writable by the application user", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_WithRelativeKeyRingPath_AlsoRefusesToStart()
    {
        // The path is optional outside Production, but a configured one is never silently ignored.
        var ex = Assert.Throws<InvalidOperationException>(() => BuildProvider("Development", keyRingPath: "keys"));

        Assert.Equal("DataProtection__KeyRingPath must be an absolute path.", ex.Message);
    }

    [Fact]
    public void Development_WithoutKeyRingPath_KeepsTheFrameworkDefault()
    {
        using var provider = BuildProvider("Development", keyRingPath: null);

        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "keys")));
    }

    [Fact]
    public void ConfiguredKeyRing_IsPersisted_AndReadableByANewProcess()
    {
        var keyRingPath = Path.Combine(_tempRoot, "keys");

        string protectedPayload;
        using (var firstProcess = BuildProvider("Production", keyRingPath))
        {
            protectedPayload = firstProcess.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("session").Protect("auth-cookie-payload");
        }

        Assert.Single(Directory.GetFiles(keyRingPath, "key-*.xml"));

        using var recreatedProcess = BuildProvider("Production", keyRingPath);
        var unprotected = recreatedProcess.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("session").Unprotect(protectedPayload);

        Assert.Equal("auth-cookie-payload", unprotected);
        Assert.Single(Directory.GetFiles(keyRingPath, "key-*.xml"));
    }

    [Fact]
    public void ConfiguredKeyRing_IsolatesKeysUnderTheClientProfileApplicationName()
    {
        using var provider = BuildProvider("Production", Path.Combine(_tempRoot, "keys"));

        Assert.Equal(
            ClientProfileLoader.Embedded.Identity.DataProtectionApplicationName,
            provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    [Fact]
    public void KeyRing_IsNotReadableFromADifferentKeyDirectory()
    {
        string protectedPayload;
        using (var firstProcess = BuildProvider("Production", Path.Combine(_tempRoot, "keys")))
        {
            protectedPayload = firstProcess.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("session").Protect("auth-cookie-payload");
        }

        using var otherProcess = BuildProvider("Production", Path.Combine(_tempRoot, "other-keys"));
        var protector = otherProcess.GetRequiredService<IDataProtectionProvider>().CreateProtector("session");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => protector.Unprotect(protectedPayload));
    }

    private ServiceProvider BuildProvider(string environmentName, string? keyRingPath)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=unused.invalid;Database=unused;Username=unused;Password=unused",
            ["Documents:Issuer:LegalName"] = "Acme Test Fixture, Lda.",
            ["Documents:Issuer:Address"] = "Avenida de Teste 10, 1000-100 Lisboa",
            ["Documents:Issuer:TaxId"] = "500100200",
            ["Email:Smtp:Host"] = "smtp.example.test",
            ["Email:Smtp:FromAddress"] = "equipamentos@example.test",
            ["Email:Smtp:FromDisplayName"] = "Maria Fictícia",
            ["Email:Signature:Name"] = "Maria Fictícia",
            ["Email:Signature:Role"] = "Técnica de Recursos Humanos",
            ["Email:Signature:Company"] = "Acme",
            ["App:PublicBaseUrl"] = "https://equipment.example.test",
            ["PrivateStorage:RootPath"] = Path.Combine(_tempRoot, "storage"),
            ["DataProtection:KeyRingPath"] = keyRingPath,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration, new FakeHostEnvironment(environmentName, _tempRoot));
        return services.BuildServiceProvider();
    }

    private sealed class FakeHostEnvironment(string environmentName, string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "EquipmentManagement.UnitTests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
