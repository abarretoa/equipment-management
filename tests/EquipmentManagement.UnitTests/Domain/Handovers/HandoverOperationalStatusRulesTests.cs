using EquipmentManagement.Domain.Handovers;

namespace EquipmentManagement.UnitTests.Domain.Handovers;

public class HandoverOperationalStatusRulesTests
{
    [Fact]
    public void Derive_DraftIsAlwaysDraft_RegardlessOfSignatureFlag()
    {
        Assert.Equal(HandoverOperationalStatus.Draft, HandoverOperationalStatusRules.Derive(HandoverStatus.Draft, hasSignedCurrentTerm: false));
        Assert.Equal(HandoverOperationalStatus.Draft, HandoverOperationalStatusRules.Derive(HandoverStatus.Draft, hasSignedCurrentTerm: true));
    }

    [Fact]
    public void Derive_FinalizedWithoutSignedCurrentTerm_IsAwaitingSignedTerm()
    {
        Assert.Equal(
            HandoverOperationalStatus.AwaitingSignedTerm,
            HandoverOperationalStatusRules.Derive(HandoverStatus.Finalized, hasSignedCurrentTerm: false));
    }

    [Fact]
    public void Derive_FinalizedWithSignedCurrentTerm_IsCompleted()
    {
        Assert.Equal(
            HandoverOperationalStatus.Completed,
            HandoverOperationalStatusRules.Derive(HandoverStatus.Finalized, hasSignedCurrentTerm: true));
    }
}
