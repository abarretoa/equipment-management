using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.ResponsibilityTerms;

namespace EquipmentManagement.UnitTests.Domain.ResponsibilityTerms;

public class ResponsibilityTermRulesTests
{
    [Theory]
    [InlineData(HandoverDeliveryMethod.InPerson, false)]
    [InlineData(HandoverDeliveryMethod.Carrier, true)]
    public void CanGenerate_TrueForAnyFinalizedHandover(HandoverDeliveryMethod method, bool hasDispatch)
    {
        Assert.True(ResponsibilityTermRules.CanGenerate(HandoverStatus.Finalized, method, hasDispatch));
    }

    [Fact]
    public void CanGenerate_TrueForADispatchedCarrierDraft()
    {
        Assert.True(ResponsibilityTermRules.CanGenerate(HandoverStatus.Draft, HandoverDeliveryMethod.Carrier, hasDispatch: true));
        Assert.True(ResponsibilityTermRules.CanPrepareBeforeDelivery(HandoverStatus.Draft, HandoverDeliveryMethod.Carrier, hasDispatch: true));
    }

    [Theory]
    [InlineData(HandoverDeliveryMethod.Carrier, false)]
    [InlineData(HandoverDeliveryMethod.InPerson, false)]
    [InlineData(HandoverDeliveryMethod.InPerson, true)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void CanGenerate_FalseForAnyOtherDraft(HandoverDeliveryMethod? method, bool hasDispatch)
    {
        Assert.False(ResponsibilityTermRules.CanGenerate(HandoverStatus.Draft, method, hasDispatch));
        Assert.False(ResponsibilityTermRules.CanPrepareBeforeDelivery(HandoverStatus.Draft, method, hasDispatch));
    }

    [Fact]
    public void CanPrepareBeforeDelivery_FalseOnceFinalized()
    {
        Assert.False(ResponsibilityTermRules.CanPrepareBeforeDelivery(HandoverStatus.Finalized, HandoverDeliveryMethod.Carrier, hasDispatch: true));
    }

    [Fact]
    public void AllowsSignatureWorkflow_OnlyAfterPhysicalReceiptIsConfirmed()
    {
        Assert.True(ResponsibilityTermRules.AllowsSignatureWorkflow(HandoverStatus.Finalized));
        Assert.False(ResponsibilityTermRules.AllowsSignatureWorkflow(HandoverStatus.Draft));
    }
}
