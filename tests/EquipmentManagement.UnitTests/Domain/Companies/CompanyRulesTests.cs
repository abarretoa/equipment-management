using EquipmentManagement.Domain.Companies;

namespace EquipmentManagement.UnitTests.Domain.Companies;

public class CompanyRulesTests
{
    [Theory]
    [InlineData("Example Client", true)]
    [InlineData("  ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidName_RejectsBlankNames(string? name, bool expected)
    {
        Assert.Equal(expected, CompanyRules.IsValidName(name));
    }

    [Theory]
    [InlineData("Example Client", "example client", true)]
    [InlineData("  Example Client  ", "EXAMPLE CLIENT", true)]
    [InlineData("Example Client Ltd", "example client   ltd", true)]
    [InlineData("Example Client", "Other Client", false)]
    public void NormalizeName_IsCaseInsensitiveAndWhitespaceInsensitive(string a, string b, bool expectEqual)
    {
        Assert.Equal(expectEqual, CompanyRules.NormalizeName(a) == CompanyRules.NormalizeName(b));
    }
}
