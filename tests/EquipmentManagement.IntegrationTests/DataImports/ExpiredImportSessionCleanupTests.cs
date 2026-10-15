using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Infrastructure.DataImports;
using EquipmentManagement.Infrastructure.Persistence;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EquipmentManagement.IntegrationTests.DataImports;

/// <summary>
/// <see cref="ExpiredImportSessionCleaner"/> (ADR-077) against a real PostgreSQL
/// database, with a fixed clock — never a real timer. Every session here
/// expires in 2001 and the clock is set there too, so the cleaner can only ever
/// match this class's own synthetic sessions, never another test's.
/// </summary>
public sealed class ExpiredImportSessionCleanupTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string Snapshot = """[{"fullName":"Synthetic Person","nif":"000000000"}]""";
    private static readonly DateTime Now = new(2001, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExpiredPendingSession_IsCleared_KeepingItsMetadata()
    {
        var expired = await InsertAsync(ImportSessionStatus.Pending, Now.AddMinutes(-1));

        await ClearAsync();

        var after = await LoadAsync(expired.Id);
        Assert.Null(after.RowsJson);
        Assert.Equal(ImportSessionStatus.Pending, after.Status);
        Assert.Equal(expired.FileSha256, after.FileSha256);
        Assert.Equal(expired.OriginalFileName, after.OriginalFileName);
        Assert.Equal(expired.TotalRows, after.TotalRows);
        Assert.Equal(expired.ExpiresAtUtc, after.ExpiresAtUtc);
        Assert.Equal(expired.CreatedAtUtc, after.CreatedAtUtc);
        Assert.Equal(expired.CreatedByUserId, after.CreatedByUserId);
    }

    [Fact]
    public async Task UnexpiredPendingSession_KeepsItsSnapshot()
    {
        var active = await InsertAsync(ImportSessionStatus.Pending, Now.AddMinutes(1));

        await ClearAsync();

        Assert.Contains("Synthetic Person", (await LoadAsync(active.Id)).RowsJson);
    }

    [Fact]
    public async Task Boundary_ExpiresExactlyNow_IsNotYetExpired_AndOneTickLaterIs()
    {
        // Same rule as ImportSessionRules.IsExpired: expired only strictly after ExpiresAtUtc.
        var atBoundary = await InsertAsync(ImportSessionStatus.Pending, Now);
        var justPast = await InsertAsync(ImportSessionStatus.Pending, Now.AddTicks(-10));

        await ClearAsync();

        Assert.NotNull((await LoadAsync(atBoundary.Id)).RowsJson);
        Assert.Null((await LoadAsync(justPast.Id)).RowsJson);
    }

    [Fact]
    public async Task RunningTwice_IsIdempotent()
    {
        var expired = await InsertAsync(ImportSessionStatus.Pending, Now.AddHours(-2));

        await ClearAsync();
        var secondRun = await ClearAsync();

        Assert.Null((await LoadAsync(expired.Id)).RowsJson);
        Assert.Equal(0, secondRun);
    }

    [Theory]
    [InlineData(ImportSessionStatus.Completed)]
    [InlineData(ImportSessionStatus.Cancelled)]
    public async Task TerminalSessions_AreNeverTouched(ImportSessionStatus status)
    {
        // Terminal sessions already have RowsJson cleared by Complete/Cancel; a
        // non-null value is forced here only to prove the cleaner's status filter.
        var terminal = await InsertAsync(status, Now.AddDays(-1));

        await ClearAsync();

        var after = await LoadAsync(terminal.Id);
        Assert.Equal(status, after.Status);
        Assert.NotNull(after.RowsJson);
    }

    [Fact]
    public async Task CleanerDoesNotResurrectOrCancel_AnExpiredSessionStaysUnconfirmablePending()
    {
        var expired = await InsertAsync(ImportSessionStatus.Pending, Now.AddMinutes(-5));

        await ClearAsync();

        var after = await LoadAsync(expired.Id);
        Assert.Equal(ImportSessionStatus.Pending, after.Status);
        Assert.Null(after.ConfirmedAtUtc);
        Assert.False(ImportSessionRules.CanConfirm(after.Status, after.ErrorRows, after.ExpiresAtUtc, Now));
    }

    [Fact]
    public void Worker_IsNotStartedInTheTestingHost()
    {
        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), s => s is ExpiredImportSessionCleanupWorker);
    }

    // --- helpers -----------------------------------------------------------

    private async Task<int> ClearAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await new ExpiredImportSessionCleaner(db, new FixedTimeProvider(Now)).ClearExpiredAsync();
    }

    private async Task<ImportSession> InsertAsync(ImportSessionStatus status, DateTime expiresAtUtc)
    {
        var session = new ImportSession
        {
            Id = Guid.NewGuid(),
            ImportType = ImportType.Consultants,
            Status = status,
            OriginalFileName = "synthetic.xlsx",
            FileSha256 = new string('a', 64),
            TotalRows = 1,
            ValidRows = 1,
            RowsJson = Snapshot,
            ExpiresAtUtc = expiresAtUtc,
            CreatedByUserId = Guid.NewGuid(),
            CreatedByDisplayNameSnapshot = "Synthetic Creator",
            CreatedAtUtc = expiresAtUtc.AddHours(-24),
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ImportSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    private async Task<ImportSession> LoadAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ImportSessions.AsNoTracking().SingleAsync(s => s.Id == id);
    }


    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
