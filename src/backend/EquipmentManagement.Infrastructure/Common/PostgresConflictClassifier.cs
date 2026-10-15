using Npgsql;

namespace EquipmentManagement.Infrastructure.Common;

/// <summary>
/// Finds the Postgres error underlying an exception raised by EF Core/Npgsql, by
/// walking the full <see cref="Exception.InnerException"/> chain instead of matching
/// one specific wrapper shape.
///
/// The same underlying Postgres condition (a unique-index violation, a serialization
/// failure under Serializable isolation) can surface differently depending on where
/// EF Core/Npgsql detects it: as a raw <see cref="PostgresException"/> (e.g. at
/// transaction commit), as a <c>DbUpdateException</c> wrapping it (during
/// SaveChangesAsync), or with an additional outer wrapper. A real concurrent-request
/// integration test in Phase 4 (finalizing the same equipment from two requests at
/// once) proved all three shapes occur for the exact same underlying conflict — see
/// docs/ARCHITECTURE_DECISIONS.md. Every service that maps an expected Postgres
/// conflict to a safe domain result (a duplicate unique field, a Serializable-isolation
/// concurrency conflict) should use this instead of matching one exception type.
/// </summary>
public static class PostgresConflictClassifier
{
    /// <summary>
    /// Returns the first <see cref="PostgresException"/> in <paramref name="exception"/>'s
    /// chain whose SQLSTATE is one of <paramref name="sqlStates"/>, or <see langword="null"/>
    /// otherwise — including when <paramref name="exception"/> is an unrelated failure
    /// (a foreign-key/not-null/check violation, a connection failure, a command
    /// timeout, an arbitrary nested exception, ...), which must keep propagating as an
    /// unexpected exception rather than being classified as a business conflict.
    /// </summary>
    public static PostgresException? Find(Exception exception, params string[] sqlStates)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && Array.IndexOf(sqlStates, postgres.SqlState) >= 0)
            {
                return postgres;
            }
        }

        return null;
    }

    public static bool IsUniqueViolation(Exception exception) =>
        Find(exception, PostgresErrorCodes.UniqueViolation) is not null;

    /// <summary>
    /// Like <see cref="IsUniqueViolation(Exception)"/>, but only for the named unique
    /// constraint/index — use this when an operation could violate more than one
    /// unique constraint, so a caller can map each one to its own domain result (and
    /// let violations of any other constraint propagate as unexpected).
    /// </summary>
    public static bool IsUniqueViolationOn(Exception exception, string constraintName) =>
        Find(exception, PostgresErrorCodes.UniqueViolation) is { } postgres &&
        string.Equals(postgres.ConstraintName, constraintName, StringComparison.Ordinal);

    public static bool IsSerializationFailure(Exception exception) =>
        Find(exception, PostgresErrorCodes.SerializationFailure) is not null;
}
