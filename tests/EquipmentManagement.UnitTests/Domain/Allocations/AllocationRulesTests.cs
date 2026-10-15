using EquipmentManagement.Domain.Allocations;

namespace EquipmentManagement.UnitTests.Domain.Allocations;

public class AllocationRulesTests
{
    [Theory]
    [InlineData("Developer", true)]
    [InlineData("  ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidRoleOrFunction_RejectsBlankValues(string? roleOrFunction, bool expected)
    {
        Assert.Equal(expected, AllocationRules.IsValidRoleOrFunction(roleOrFunction));
    }

    [Fact]
    public void ExternalAllocation_RequiresACompany()
    {
        Assert.True(AllocationRules.IsCompanyRequirementSatisfied(WorkforceType.External, Guid.NewGuid()));
        Assert.False(AllocationRules.IsCompanyRequirementSatisfied(WorkforceType.External, null));
    }

    [Fact]
    public void InternalAllocation_MustNotHaveACompany()
    {
        Assert.True(AllocationRules.IsCompanyRequirementSatisfied(WorkforceType.Internal, null));
        Assert.False(AllocationRules.IsCompanyRequirementSatisfied(WorkforceType.Internal, Guid.NewGuid()));
    }

    [Fact]
    public void IsValidDateRange_AllowsNullEndDate()
    {
        Assert.True(AllocationRules.IsValidDateRange(new DateOnly(2026, 1, 1), null));
    }

    [Fact]
    public void IsValidDateRange_RejectsEndBeforeStart()
    {
        Assert.False(AllocationRules.IsValidDateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void IsValidDateRange_AllowsEndEqualToStart()
    {
        Assert.True(AllocationRules.IsValidDateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void CanCreateActiveAllocation_TrueOnlyWhenNoActiveAllocationExists()
    {
        Assert.True(AllocationRules.CanCreateActiveAllocation(0));
        Assert.False(AllocationRules.CanCreateActiveAllocation(1));
        Assert.False(AllocationRules.CanCreateActiveAllocation(2));
    }
}
