using EquipmentManagement.Domain.Consultants;

namespace EquipmentManagement.UnitTests.Domain.Consultants;

public class ConsultantRulesTests
{
    [Theory]
    [InlineData("João Exemplo", true)]
    [InlineData("  ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidFullName_RejectsBlankNames(string? fullName, bool expected)
    {
        Assert.Equal(expected, ConsultantRules.IsValidFullName(fullName));
    }

    [Theory]
    [InlineData("123456789", "123456789")]
    [InlineData(" 123 456 789 ", "123456789")]
    [InlineData("PT123456789", "123456789")]
    [InlineData("123-456-789", "123456789")]
    public void NormalizeNif_StripsEverythingButDigits(string input, string expected)
    {
        Assert.Equal(expected, ConsultantRules.NormalizeNif(input));
    }

    [Theory]
    [InlineData("123456789", true)] // 9 digits, the common Portuguese case
    [InlineData("1234", false)] // below the conservative minimum
    [InlineData("", false)]
    [InlineData("123456789012345678901", false)] // above the conservative maximum
    [InlineData("12345", true)] // shorter foreign-style id, deliberately not rejected — see ConsultantRules remarks
    public void IsValidNormalizedNif_UsesConservativeLengthBoundsNotAPortugueseChecksum(string normalizedNif, bool expected)
    {
        Assert.Equal(expected, ConsultantRules.IsValidNormalizedNif(normalizedNif));
    }
}
