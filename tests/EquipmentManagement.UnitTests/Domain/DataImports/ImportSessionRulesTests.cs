using EquipmentManagement.Domain.DataImports;

namespace EquipmentManagement.UnitTests.Domain.DataImports;

public class ImportSessionRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CanConfirm_TrueForPendingNoErrorsWithinWindow()
    {
        Assert.True(ImportSessionRules.CanConfirm(ImportSessionStatus.Pending, errorRows: 0, expiresAtUtc: Now.AddHours(1), nowUtc: Now));
    }

    [Fact]
    public void CanConfirm_FalseWhenErrorRowsExist()
    {
        Assert.False(ImportSessionRules.CanConfirm(ImportSessionStatus.Pending, errorRows: 1, expiresAtUtc: Now.AddHours(1), nowUtc: Now));
    }

    [Theory]
    [InlineData(ImportSessionStatus.Completed)]
    [InlineData(ImportSessionStatus.Cancelled)]
    public void CanConfirm_FalseForNonPendingStatus(ImportSessionStatus status)
    {
        Assert.False(ImportSessionRules.CanConfirm(status, errorRows: 0, expiresAtUtc: Now.AddHours(1), nowUtc: Now));
    }

    [Fact]
    public void CanConfirm_FalseAfterExpiry()
    {
        var expiresAtUtc = Now - TimeSpan.FromMinutes(1);
        Assert.False(ImportSessionRules.CanConfirm(ImportSessionStatus.Pending, errorRows: 0, expiresAtUtc, Now));
    }

    [Fact]
    public void CanConfirm_TrueExactlyAtExpiryBoundary()
    {
        // IsExpired is strictly "nowUtc > expiresAtUtc", so the exact instant of expiry itself is still confirmable.
        Assert.True(ImportSessionRules.CanConfirm(ImportSessionStatus.Pending, errorRows: 0, expiresAtUtc: Now, nowUtc: Now));
    }

    [Fact]
    public void IsExpired_TrueOnlyStrictlyAfterExpiresAtUtc()
    {
        Assert.False(ImportSessionRules.IsExpired(expiresAtUtc: Now, nowUtc: Now));
        Assert.True(ImportSessionRules.IsExpired(expiresAtUtc: Now, nowUtc: Now.AddTicks(1)));
    }

    [Theory]
    [InlineData(ImportSessionStatus.Completed)]
    [InlineData(ImportSessionStatus.Cancelled)]
    public void IsExpiredPending_FalseForNonPendingEvenPastExpiry(ImportSessionStatus status)
    {
        Assert.False(ImportSessionRules.IsExpiredPending(status, expiresAtUtc: Now.AddHours(-1), nowUtc: Now));
    }

    [Fact]
    public void IsExpiredPending_TrueForPendingPastExpiry()
    {
        Assert.True(ImportSessionRules.IsExpiredPending(ImportSessionStatus.Pending, expiresAtUtc: Now.AddHours(-1), nowUtc: Now));
    }

    [Fact]
    public void CanCancel_TrueOnlyForPending()
    {
        Assert.True(ImportSessionRules.CanCancel(ImportSessionStatus.Pending));
        Assert.False(ImportSessionRules.CanCancel(ImportSessionStatus.Completed));
        Assert.False(ImportSessionRules.CanCancel(ImportSessionStatus.Cancelled));
    }
}
