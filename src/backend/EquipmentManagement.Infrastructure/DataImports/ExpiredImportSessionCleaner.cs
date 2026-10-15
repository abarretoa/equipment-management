using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Clears the preview PII snapshot (<see cref="ImportSession.RowsJson"/>) of
/// every still-<see cref="ImportSessionStatus.Pending"/> session past its
/// <see cref="ImportSession.ExpiresAtUtc"/> — the same rule
/// (<see cref="ImportSessionRules.IsExpiredPending"/>) the import services apply
/// lazily on access, applied here without waiting for anyone to revisit an
/// abandoned preview (ADR-077). Status, counts, FileSha256, timestamps and
/// creator stay as they are: the session is not cancelled or deleted, and an
/// expired session already cannot be confirmed.
/// </summary>
/// <remarks>
/// One set-based UPDATE (backed by the existing ExpiresAtUtc index), so it is
/// atomic and idempotent: an already-cleared row is not matched again, and a
/// concurrent lazy clear or cancel just leaves nothing for this one to do.
/// Invoked on a schedule by <see cref="ExpiredImportSessionCleanupWorker"/>;
/// tests call it directly with a fixed clock.
/// </remarks>
public sealed class ExpiredImportSessionCleaner(ApplicationDbContext dbContext, TimeProvider timeProvider)
{
    /// <summary>Returns how many sessions had their snapshot cleared.</summary>
    public Task<int> ClearExpiredAsync(CancellationToken cancellationToken = default)
    {
        // IsExpired is "now > ExpiresAtUtc"; this is the same comparison.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return dbContext.ImportSessions
            .Where(s => s.Status == ImportSessionStatus.Pending && s.ExpiresAtUtc < now && s.RowsJson != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RowsJson, (string?)null), cancellationToken);
    }
}
