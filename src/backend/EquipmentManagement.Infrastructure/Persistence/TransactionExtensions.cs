using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EquipmentManagement.Infrastructure.Persistence;

/// <summary>
/// Keeps a mutation and its required audit row in one database transaction
/// (ADR-076). <see cref="Auditing.AuditService.RecordAsync"/> saves on its own,
/// so a service that saves its change first and audits second must wrap both
/// SaveChanges calls in a transaction, or a failed audit insert leaves the
/// change committed with no record of who made it.
/// </summary>
/// <remarks>
/// Some audited service methods also run inside a caller's transaction (e.g.
/// <c>EquipmentService.RetireAsync</c> under
/// <c>EquipmentRetirementRequestService.ApproveAsync</c>), where a second
/// BeginTransaction would throw. <see cref="BeginTransactionIfNoneAsync"/>
/// joins the caller's transaction instead — the outer owner commits — and only
/// the transaction it actually opened is committed by
/// <see cref="CommitIfOwnedAsync"/>. A SaveChanges failure inside a transaction
/// rolls back to EF Core's automatic savepoint, so the existing
/// unique-violation catch-and-return paths keep working unchanged; returning
/// without committing disposes (rolls back) the transaction.
/// </remarks>
public static class TransactionExtensions
{
    /// <summary>Begins a transaction, or returns null when one is already open on this context.</summary>
    public static async Task<IDbContextTransaction?> BeginTransactionIfNoneAsync(
        this DatabaseFacade database, CancellationToken cancellationToken = default) =>
        database.CurrentTransaction is null ? await database.BeginTransactionAsync(cancellationToken) : null;

    /// <summary>
    /// Begins a transaction at <paramref name="isolationLevel"/>, or returns null
    /// when one is already open on this context. A caller that joins must
    /// already run at that level (or stricter); the outer owner commits and is
    /// the one that translates a conflict at its own boundary.
    /// </summary>
    public static async Task<IDbContextTransaction?> BeginTransactionIfNoneAsync(
        this DatabaseFacade database, IsolationLevel isolationLevel, CancellationToken cancellationToken = default) =>
        database.CurrentTransaction is null ? await database.BeginTransactionAsync(isolationLevel, cancellationToken) : null;

    /// <summary>Commits <paramref name="transaction"/> when this caller opened it; a no-op when it joined an outer one.</summary>
    public static Task CommitIfOwnedAsync(this IDbContextTransaction? transaction, CancellationToken cancellationToken = default) =>
        transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;
}
