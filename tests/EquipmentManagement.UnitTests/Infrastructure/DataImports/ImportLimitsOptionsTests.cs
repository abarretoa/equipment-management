using System.ComponentModel.DataAnnotations;
using EquipmentManagement.Infrastructure.DataImports;

namespace EquipmentManagement.UnitTests.Infrastructure.DataImports;

/// <summary>The expired-session cleanup interval (ADR-077) is validated at startup via data annotations.</summary>
public sealed class ImportLimitsOptionsTests
{
    [Fact]
    public void Default_IsAnHour_AndValid()
    {
        var options = new ImportLimitsOptions();

        Assert.Equal(60, options.ExpiredSessionCleanupIntervalMinutes);
        Assert.True(IsValid(options));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1440, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(1441, false)]
    public void Interval_MustBeBetweenOneMinuteAndOneDay(int minutes, bool expected)
    {
        Assert.Equal(expected, IsValid(new ImportLimitsOptions { ExpiredSessionCleanupIntervalMinutes = minutes }));
    }

    private static bool IsValid(ImportLimitsOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);
}
