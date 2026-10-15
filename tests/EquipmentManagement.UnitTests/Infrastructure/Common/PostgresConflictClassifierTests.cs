using EquipmentManagement.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EquipmentManagement.UnitTests.Infrastructure.Common;

public sealed class PostgresConflictClassifierTests
{
    [Fact]
    public void Find_MatchesRawPostgresException()
    {
        var pg = MakePostgresException(PostgresErrorCodes.UniqueViolation);

        Assert.Same(pg, PostgresConflictClassifier.Find(pg, PostgresErrorCodes.UniqueViolation));
    }

    [Fact]
    public void Find_MatchesPostgresExceptionWrappedInDbUpdateException()
    {
        var pg = MakePostgresException(PostgresErrorCodes.UniqueViolation);
        var wrapped = new DbUpdateException("Saving changes failed.", pg);

        Assert.Same(pg, PostgresConflictClassifier.Find(wrapped, PostgresErrorCodes.UniqueViolation));
    }

    [Fact]
    public void Find_MatchesPostgresExceptionThroughDoubleWrapping()
    {
        // Reproduces the exact shape Phase 4's concurrent-finalize integration test
        // observed: EF's execution strategy adds an outer InvalidOperationException
        // around the DbUpdateException when a user-managed transaction is active.
        var pg = MakePostgresException(PostgresErrorCodes.SerializationFailure);
        var dbUpdate = new DbUpdateException("Saving changes failed.", pg);
        var outer = new InvalidOperationException(
            "An exception has occurred while iterating through a query result... likely due to a transient failure.", dbUpdate);

        Assert.Same(pg, PostgresConflictClassifier.Find(outer, PostgresErrorCodes.SerializationFailure));
    }

    [Fact]
    public void Find_ReturnsNull_ForUnrelatedException()
    {
        var ex = new InvalidOperationException("Something else went wrong.");

        Assert.Null(PostgresConflictClassifier.Find(ex, PostgresErrorCodes.UniqueViolation));
    }

    [Fact]
    public void Find_ReturnsNull_ForDbUpdateExceptionWrappingADifferentSqlState()
    {
        // A foreign-key violation (23503) must never be classified as a unique
        // conflict, however deep the wrapping.
        var pg = MakePostgresException(PostgresErrorCodes.ForeignKeyViolation);
        var wrapped = new DbUpdateException("Saving changes failed.", pg);

        Assert.Null(PostgresConflictClassifier.Find(wrapped, PostgresErrorCodes.UniqueViolation));
    }

    [Fact]
    public void IsUniqueViolation_ReturnsFalse_ForUnrelatedDbUpdateException()
    {
        // A DbUpdateException whose inner exception is not even a PostgresException
        // (e.g. a plain provider error) must not be classified as a conflict —
        // this guards against "classify every DbUpdateException" regressions.
        var wrapped = new DbUpdateException("Saving changes failed.", new InvalidOperationException("connection reset"));

        Assert.False(PostgresConflictClassifier.IsUniqueViolation(wrapped));
    }

    [Fact]
    public void IsUniqueViolationOn_MatchesOnlyTheNamedConstraint()
    {
        var pg = MakePostgresException(PostgresErrorCodes.UniqueViolation, constraintName: "IX_Equipment_NormalizedSerialNumber");
        var wrapped = new DbUpdateException("Saving changes failed.", pg);

        Assert.True(PostgresConflictClassifier.IsUniqueViolationOn(wrapped, "IX_Equipment_NormalizedSerialNumber"));
        Assert.False(PostgresConflictClassifier.IsUniqueViolationOn(wrapped, "IX_Equipment_AssetTag"));
    }

    [Fact]
    public void IsSerializationFailure_MatchesWrappedSerializationConflict()
    {
        var pg = MakePostgresException(PostgresErrorCodes.SerializationFailure);
        var wrapped = new DbUpdateException("Saving changes failed.", pg);

        Assert.True(PostgresConflictClassifier.IsSerializationFailure(wrapped));
        Assert.False(PostgresConflictClassifier.IsUniqueViolation(wrapped));
    }

    private static PostgresException MakePostgresException(string sqlState, string? constraintName = null) =>
        new("simulated failure", "ERROR", "ERROR", sqlState, constraintName: constraintName);
}
