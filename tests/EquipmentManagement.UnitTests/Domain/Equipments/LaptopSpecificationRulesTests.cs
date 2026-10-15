using EquipmentManagement.Domain.Equipments;

namespace EquipmentManagement.UnitTests.Domain.Equipments;

public class LaptopSpecificationRulesTests
{
    [Fact]
    public void IsComplete_TrueWhenAllThreeFieldsPresent()
    {
        Assert.True(LaptopSpecificationRules.IsComplete("Intel i7", "16 GB", "512 GB SSD"));
    }

    [Theory]
    [InlineData(null, "16 GB", "512 GB SSD")]
    [InlineData("Intel i7", null, "512 GB SSD")]
    [InlineData("Intel i7", "16 GB", null)]
    [InlineData("", "16 GB", "512 GB SSD")]
    [InlineData("Intel i7", "  ", "512 GB SSD")]
    public void IsComplete_FalseWhenAnyFieldMissing(string? processor, string? memory, string? storage)
    {
        Assert.False(LaptopSpecificationRules.IsComplete(processor, memory, storage));
    }
}
