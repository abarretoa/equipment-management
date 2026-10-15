using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.UnitTests.Domain.Equipments;

public class AssetTagFormatterTests
{
    [Theory]
    [InlineData("LT", 23, "LT-0023")]
    [InlineData("MN", 1, "MN-0001")]
    [InlineData("EQ", 9999, "EQ-9999")]
    [InlineData("EQ", 10000, "EQ-10000")] // overflow beyond 4 digits is not truncated
    public void Format_ProducesPrefixDashZeroPaddedNumber(string prefix, long sequenceValue, string expected)
    {
        Assert.Equal(expected, AssetTagFormatter.Format(prefix, sequenceValue));
    }
}
