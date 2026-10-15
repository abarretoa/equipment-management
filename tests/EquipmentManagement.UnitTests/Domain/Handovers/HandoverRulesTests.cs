using EquipmentManagement.Domain.Handovers;

namespace EquipmentManagement.UnitTests.Domain.Handovers;

public class HandoverRulesTests
{
    [Fact]
    public void CanModify_TrueOnlyForDraft()
    {
        Assert.True(HandoverRules.CanModify(HandoverStatus.Draft));
        Assert.False(HandoverRules.CanModify(HandoverStatus.Finalized));
    }

    [Fact]
    public void CanFinalize_TrueOnlyForDraft()
    {
        Assert.True(HandoverRules.CanFinalize(HandoverStatus.Draft));
        Assert.False(HandoverRules.CanFinalize(HandoverStatus.Finalized));
    }

    [Fact]
    public void IsValidDeliveredOn_FalseWhenNull()
    {
        var today = new DateOnly(2026, 9, 8);
        Assert.False(HandoverRules.IsValidDeliveredOn(null, today));
    }

    [Fact]
    public void IsValidDeliveredOn_FalseWhenInTheFuture()
    {
        var today = new DateOnly(2026, 9, 8);
        Assert.False(HandoverRules.IsValidDeliveredOn(new DateOnly(2026, 9, 9), today));
    }

    [Theory]
    [InlineData(2026, 9, 8)]
    [InlineData(2026, 9, 7)]
    public void IsValidDeliveredOn_TrueForTodayOrEarlier(int year, int month, int day)
    {
        var today = new DateOnly(2026, 9, 8);
        Assert.True(HandoverRules.IsValidDeliveredOn(new DateOnly(year, month, day), today));
    }

    [Fact]
    public void CanModifyComposition_TrueForDraftWithoutDispatch()
    {
        Assert.True(HandoverRules.CanModifyComposition(HandoverStatus.Draft, hasDispatch: false));
    }

    [Fact]
    public void CanModifyComposition_FalseForDraftWithDispatch()
    {
        Assert.False(HandoverRules.CanModifyComposition(HandoverStatus.Draft, hasDispatch: true));
    }

    [Fact]
    public void CanModifyComposition_FalseForFinalized()
    {
        Assert.False(HandoverRules.CanModifyComposition(HandoverStatus.Finalized, hasDispatch: false));
    }

    [Fact]
    public void CanChangeDeliveryMethod_OnlyForDraftWithoutDispatch()
    {
        Assert.True(HandoverRules.CanChangeDeliveryMethod(HandoverStatus.Draft, hasDispatch: false));
        Assert.False(HandoverRules.CanChangeDeliveryMethod(HandoverStatus.Draft, hasDispatch: true));
        Assert.False(HandoverRules.CanChangeDeliveryMethod(HandoverStatus.Finalized, hasDispatch: false));
    }

    [Fact]
    public void CanDeleteDraft_OnlyForAPurelyPreOperationalDraft()
    {
        Assert.True(HandoverRules.CanDeleteDraft(HandoverStatus.Draft, hasDispatch: false, hasAssignment: false, hasResponsibilityTerm: false));
    }

    [Theory]
    [InlineData(HandoverStatus.Finalized, false, false, false)]
    [InlineData(HandoverStatus.Finalized, false, true, true)]
    [InlineData(HandoverStatus.Draft, true, false, false)]
    [InlineData(HandoverStatus.Draft, false, true, false)]
    [InlineData(HandoverStatus.Draft, false, false, true)]
    public void CanDeleteDraft_FalseOnceOperationallyLocked_OrWithUnexpectedArtifacts(
        HandoverStatus status, bool hasDispatch, bool hasAssignment, bool hasResponsibilityTerm)
    {
        Assert.False(HandoverRules.CanDeleteDraft(status, hasDispatch, hasAssignment, hasResponsibilityTerm));
    }

    [Fact]
    public void ResolveConsultantEligibility_NoActiveAllocation_IsNotEligible()
    {
        Assert.Equal(ConsultantHandoverEligibility.NoActiveAllocation, HandoverRules.ResolveConsultantEligibility(0));
    }

    [Fact]
    public void ResolveConsultantEligibility_ExactlyOneActiveAllocation_IsEligible()
    {
        Assert.Equal(ConsultantHandoverEligibility.Eligible, HandoverRules.ResolveConsultantEligibility(1));
    }

    /// <summary>Inconsistent data (impossible through the product after the partial unique index) is never silently resolved.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ResolveConsultantEligibility_MoreThanOneActiveAllocation_IsInconsistent(int activeCount)
    {
        Assert.Equal(ConsultantHandoverEligibility.MultipleActiveAllocations, HandoverRules.ResolveConsultantEligibility(activeCount));
    }

    [Fact]
    public void AllowsDispatch_OnlyForCarrier()
    {
        Assert.True(HandoverRules.AllowsDispatch(HandoverDeliveryMethod.Carrier));
        Assert.False(HandoverRules.AllowsDispatch(HandoverDeliveryMethod.InPerson));
        Assert.False(HandoverRules.AllowsDispatch(null));
    }

    [Fact]
    public void RequiresDispatchBeforeFinalize_OnlyForCarrier()
    {
        Assert.True(HandoverRules.RequiresDispatchBeforeFinalize(HandoverDeliveryMethod.Carrier));
        Assert.False(HandoverRules.RequiresDispatchBeforeFinalize(HandoverDeliveryMethod.InPerson));
    }

    [Fact]
    public void CanCancelDispatch_OnlyForACarrierDraftWithAnActiveDispatch()
    {
        Assert.True(HandoverRules.CanCancelDispatch(HandoverStatus.Draft, HandoverDeliveryMethod.Carrier, hasActiveDispatch: true));

        Assert.False(HandoverRules.CanCancelDispatch(HandoverStatus.Draft, HandoverDeliveryMethod.Carrier, hasActiveDispatch: false));
        Assert.False(HandoverRules.CanCancelDispatch(HandoverStatus.Finalized, HandoverDeliveryMethod.Carrier, hasActiveDispatch: true));
        Assert.False(HandoverRules.CanCancelDispatch(HandoverStatus.Draft, HandoverDeliveryMethod.InPerson, hasActiveDispatch: true));
        Assert.False(HandoverRules.CanCancelDispatch(HandoverStatus.Draft, deliveryMethod: null, hasActiveDispatch: true));
    }
}
