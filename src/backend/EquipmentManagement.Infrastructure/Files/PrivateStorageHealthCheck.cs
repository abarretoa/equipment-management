using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EquipmentManagement.Infrastructure.Files;

/// <summary>
/// Phase 10 hardening: private storage (evidence photos, generated
/// and signed Responsibility Term PDFs) is a required dependency, not an
/// optional feature — without a writable store, the delivery/return/term
/// workflows this application exists for cannot function. Previously an
/// unwritable/misconfigured root only surfaced as a 500 on the first real
/// upload, sometime after deployment. This check round-trips a tiny probe
/// object through the actual configured <see cref="IPrivateFileStorage"/> —
/// local disk today, whatever object store a future implementation targets —
/// so a broken mount/permission/credential is visible at <c>/health/ready</c>
/// before any real request needs it.
/// </summary>
public sealed class PrivateStorageHealthCheck(IPrivateFileStorage storage) : IHealthCheck
{
    private const string ProbeKey = ".health-check/probe.tmp";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MemoryStream([0x01]);
            await storage.SaveAsync(ProbeKey, content, cancellationToken);
            await storage.DeleteAsync(ProbeKey, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Private file storage is not writable.", ex);
        }
    }
}
