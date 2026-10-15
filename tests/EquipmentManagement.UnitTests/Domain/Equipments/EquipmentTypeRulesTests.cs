using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.UnitTests.Domain.Equipments;

public class EquipmentTypeRulesTests
{
    [Theory]
    [InlineData("Laptop", true)]
    [InlineData("  ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidName_RejectsBlankNames(string? name, bool expected)
    {
        Assert.Equal(expected, EquipmentTypeRules.IsValidName(name));
    }

    [Theory]
    [InlineData("Laptop", "laptop", true)]
    [InlineData("  Monitor  ", "MONITOR", true)]
    [InlineData("Docking Station", "docking   station", true)]
    [InlineData("Mouse", "Keyboard", false)]
    public void NormalizeName_IsCaseInsensitiveAndWhitespaceInsensitive(string a, string b, bool expectEqual)
    {
        Assert.Equal(expectEqual, EquipmentTypeRules.NormalizeName(a) == EquipmentTypeRules.NormalizeName(b));
    }

    [Theory]
    [InlineData("lt", "LT")]
    [InlineData("  Mn  ", "MN")]
    public void NormalizePrefix_TrimsAndUpperInvariants(string input, string expected)
    {
        Assert.Equal(expected, EquipmentTypeRules.NormalizePrefix(input));
    }

    [Theory]
    [InlineData("LT", true)]
    [InlineData("MN", true)]
    [InlineData("DOCK", true)]
    [InlineData("L", false)] // too short
    [InlineData("TOOLONGX", false)] // too long
    [InlineData("L1", false)] // digits not allowed
    [InlineData("", false)]
    public void IsValidPrefix_RequiresTwoToSixLetters(string normalizedPrefix, bool expected)
    {
        Assert.Equal(expected, EquipmentTypeRules.IsValidPrefix(normalizedPrefix));
    }
}
