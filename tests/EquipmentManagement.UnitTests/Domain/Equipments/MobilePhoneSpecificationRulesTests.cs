using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.UnitTests.Domain.Equipments;

public class MobilePhoneSpecificationRulesTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("-", false)]
    [InlineData(" - - ", false)]
    [InlineData("356938035643809", true)]
    [InlineData("35-693803-564380-9", true)]
    public void IsComplete_RequiresOnlyAnImei(string? imei, bool expected)
    {
        Assert.Equal(expected, MobilePhoneSpecificationRules.IsComplete(imei));
    }

    [Theory]
    [InlineData("356938035643809")]
    [InlineData("35-693803-564380-9")]
    [InlineData("356 938 035 643 809")]
    [InlineData(" 35 6938-0356 43809 ")]
    public void IsValidImei_Accepts15Digits_WithOrWithoutSpacesOrHyphens(string imei)
    {
        Assert.True(MobilePhoneSpecificationRules.IsValidImei(imei));
        Assert.Equal("356938035643809", MobilePhoneSpecificationRules.NormalizeImei(imei));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("123")]
    [InlineData("ABC123")]
    [InlineData("35.693803/564380-9")]
    [InlineData("35693803564380")]
    [InlineData("3569380356438099")]
    [InlineData("35693803564380A")]
    [InlineData("３５６９３８０３５６４３８０９")]
    public void IsValidImei_RejectsAnythingThatIsNotExactly15AsciiDigits(string? imei)
    {
        Assert.False(MobilePhoneSpecificationRules.IsValidImei(imei));
    }

    [Theory]
    [InlineData("-")]
    [InlineData(" - - ")]
    public void SeparatorsOnly_IsMissing_NotMalformed(string imei)
    {
        Assert.False(MobilePhoneSpecificationRules.IsComplete(imei));
        Assert.False(MobilePhoneSpecificationRules.IsValidImei(imei));
    }

    [Theory]
    [InlineData("356938035643809", "356938035643809")]
    [InlineData("  356938035643809 ", "356938035643809")]
    [InlineData("35-693803-564380-9", "356938035643809")]
    [InlineData("35 693803 564380 9", "356938035643809")]
    [InlineData("ab12", "AB12")]
    public void NormalizeImei_RemovesSeparatorsAndUpperCases(string imei, string expected)
    {
        Assert.Equal(expected, MobilePhoneSpecificationRules.NormalizeImei(imei));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" - - ")]
    public void NormalizeImei_BlankOrSeparatorsOnly_IsNull(string? imei)
    {
        Assert.Null(MobilePhoneSpecificationRules.NormalizeImei(imei));
    }

    [Fact]
    public void NormalizeImei_SameImeiWrittenDifferently_Collides()
    {
        // The uniqueness key: two spellings of one IMEI must be the same value.
        Assert.Equal(
            MobilePhoneSpecificationRules.NormalizeImei("35-693803-564380-9"),
            MobilePhoneSpecificationRules.NormalizeImei("356938035643809"));
    }
}
