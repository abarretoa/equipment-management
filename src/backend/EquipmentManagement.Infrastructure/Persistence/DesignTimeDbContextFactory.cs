using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EquipmentManagement.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef` commands create an ApplicationDbContext without spinning up
/// the full API host. This factory is never used at application runtime
/// (Program.cs configures the real context from configuration instead) — but the
/// EF Core CLI tooling still routes <em>every</em> command that needs a live
/// connection (not just schema-only ones like `migrations add`) through this
/// factory when one is present, ignoring whatever `Program.cs` would otherwise
/// resolve. Phase 10 hardening: this previously hardcoded a placeholder
/// `design`/`design` connection string that pointed at a database/role nothing
/// ever provisions, silently breaking the README's documented
/// `dotnet ef database update` production-migration step (and any local
/// `migrations add`) for everyone except someone who happened to have created
/// that exact role by hand. Reads `ConnectionStrings__Default` the same way
/// every other real entry point does, falling back to the same dev-only
/// docker-compose default `appsettings.Development.json` uses — never a
/// fabricated placeholder that can't actually connect to anything.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=equipment_management;Username=equipment_management;Password=equipment_management_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
