using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.Application;

/// <summary>
/// Composition entry point for the Application layer. The API project calls this
/// once at startup; Application never references API or Infrastructure directly.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers application-layer services. Empty in Phase 0 (no use cases exist
    /// yet) — kept as the single seam future phases will add handlers/validators to,
    /// so callers never need to change beyond this one call site.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
