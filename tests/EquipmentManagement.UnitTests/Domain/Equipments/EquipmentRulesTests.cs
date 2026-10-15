using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.UnitTests.Domain.Equipments;

public class EquipmentRulesTests
{
    [Fact]
    public void RequiresManufacturer_TrueOnlyForLaptop()
    {
        Assert.True(EquipmentRules.RequiresManufacturer(EquipmentTypeKind.Laptop));
        Assert.False(EquipmentRules.RequiresManufacturer(EquipmentTypeKind.Generic));
    }

    [Fact]
    public void RequiresSerialNumber_TrueOnlyForLaptop()
    {
        Assert.True(EquipmentRules.RequiresSerialNumber(EquipmentTypeKind.Laptop));
        Assert.False(EquipmentRules.RequiresSerialNumber(EquipmentTypeKind.Generic));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("ABC123", false)]
    public void IsBlank_DetectsBlankValues(string? value, bool expected)
    {
        Assert.Equal(expected, EquipmentRules.IsBlank(value));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("abc123", "ABC123")]
    [InlineData("  AbC-123  ", "ABC-123")]
    public void NormalizeSerialNumber_TrimsAndUpperInvariantsOrReturnsNull(string? input, string? expected)
    {
        Assert.Equal(expected, EquipmentRules.NormalizeSerialNumber(input));
    }

    [Theory]
    [InlineData(EquipmentStatus.Available)]
    [InlineData(EquipmentStatus.UnderInspection)]
    [InlineData(EquipmentStatus.Damaged)]
    [InlineData(EquipmentStatus.UnderMaintenance)]
    public void ResolveCurrentPhotoCorrection_EligibleOutsideCustodyAndDelivery(EquipmentStatus status)
    {
        Assert.Equal(
            CurrentPhotoCorrectionEligibility.Eligible,
            EquipmentRules.ResolveCurrentPhotoCorrection(status, hasActiveCustody: false, isInDispatchedHandover: false));
    }

    [Theory]
    [InlineData(EquipmentStatus.Assigned)]
    [InlineData(EquipmentStatus.ReturnPending)]
    // Custody comes from the Active assignment, not the label.
    [InlineData(EquipmentStatus.Available)]
    public void ResolveCurrentPhotoCorrection_ForbiddenWhileInCustody(EquipmentStatus status)
    {
        Assert.Equal(
            CurrentPhotoCorrectionEligibility.InCustody,
            EquipmentRules.ResolveCurrentPhotoCorrection(status, hasActiveCustody: true, isInDispatchedHandover: false));
    }

    [Fact]
    public void ResolveCurrentPhotoCorrection_ForbiddenOnADispatchedCarrierHandover_EvenWithoutCustody()
    {
        Assert.Equal(
            CurrentPhotoCorrectionEligibility.InDispatchedHandover,
            EquipmentRules.ResolveCurrentPhotoCorrection(EquipmentStatus.Available, hasActiveCustody: false, isInDispatchedHandover: true));
    }

    [Fact]
    public void ResolveCurrentPhotoCorrection_ForbiddenWhileInTransit_EvenIfNoDispatchedHandoverWasFound()
    {
        Assert.Equal(
            CurrentPhotoCorrectionEligibility.InDispatchedHandover,
            EquipmentRules.ResolveCurrentPhotoCorrection(EquipmentStatus.InTransit, hasActiveCustody: false, isInDispatchedHandover: false));
    }

    [Fact]
    public void ResolveCurrentPhotoCorrection_ForbiddenOnceRetired()
    {
        Assert.Equal(
            CurrentPhotoCorrectionEligibility.Retired,
            EquipmentRules.ResolveCurrentPhotoCorrection(EquipmentStatus.Retired, hasActiveCustody: false, isInDispatchedHandover: false));
    }
}
