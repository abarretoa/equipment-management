using EquipmentManagement.Infrastructure.Identity;

namespace EquipmentManagement.UnitTests.Infrastructure.Identity;

public class TemporaryPasswordGeneratorTests
{
    [Fact]
    public void Generate_MeetsThePasswordPolicysMinimumLength()
    {
        var password = TemporaryPasswordGenerator.Generate();

        Assert.True(password.Length >= 12);
    }

    [Fact]
    public void Generate_ContainsUpperLowerAndDigitCharacters()
    {
        var password = TemporaryPasswordGenerator.Generate();

        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
    }

    [Fact]
    public void Generate_ProducesDifferentPasswordsEachTime()
    {
        var first = TemporaryPasswordGenerator.Generate();
        var second = TemporaryPasswordGenerator.Generate();

        Assert.NotEqual(first, second);
    }
}
