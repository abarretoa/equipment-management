using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Returns;

namespace EquipmentManagement.UnitTests.Domain.Returns;

public class ReturnRulesTests
{
    [Fact]
    public void CanInitiate_TrueOnlyForAssigned()
    {
        Assert.True(ReturnRules.CanInitiate(EquipmentStatus.Assigned));
        Assert.False(ReturnRules.CanInitiate(EquipmentStatus.Available));
        Assert.False(ReturnRules.CanInitiate(EquipmentStatus.ReturnPending));
        Assert.False(ReturnRules.CanInitiate(EquipmentStatus.Retired));
    }

    [Fact]
    public void ClassifyForEndOfEngagement_AssignedWithNoOpenReturn_InitiatesAReturn()
    {
        Assert.Equal(
            EndOfEngagementReturnDisposition.Initiate,
            ReturnRules.ClassifyForEndOfEngagement(EquipmentStatus.Assigned, openReturnStatus: null));
    }

    [Fact]
    public void ClassifyForEndOfEngagement_ReturnPendingWithAPendingReturn_IsAlreadyPending()
    {
        Assert.Equal(
            EndOfEngagementReturnDisposition.AlreadyPending,
            ReturnRules.ClassifyForEndOfEngagement(EquipmentStatus.ReturnPending, ReturnStatus.Pending));
    }

    [Theory]
    // No open return, but the equipment is not in a state a return can start from.
    [InlineData(EquipmentStatus.Available, null)]
    [InlineData(EquipmentStatus.ReturnPending, null)]
    [InlineData(EquipmentStatus.UnderInspection, null)]
    [InlineData(EquipmentStatus.UnderMaintenance, null)]
    [InlineData(EquipmentStatus.Damaged, null)]
    [InlineData(EquipmentStatus.Retired, null)]
    // An open return that the equipment's status does not agree with.
    [InlineData(EquipmentStatus.Assigned, ReturnStatus.Pending)]
    [InlineData(EquipmentStatus.UnderMaintenance, ReturnStatus.Pending)]
    // An open return already past "pending" on an assignment that is still Active.
    [InlineData(EquipmentStatus.ReturnPending, ReturnStatus.Received)]
    [InlineData(EquipmentStatus.UnderInspection, ReturnStatus.Received)]
    [InlineData(EquipmentStatus.Assigned, ReturnStatus.Completed)]
    [InlineData(EquipmentStatus.Available, ReturnStatus.Completed)]
    public void ClassifyForEndOfEngagement_AnyOtherCombination_RequiresAttention(EquipmentStatus equipmentStatus, ReturnStatus? openReturnStatus)
    {
        Assert.Equal(
            EndOfEngagementReturnDisposition.RequiresAttention,
            ReturnRules.ClassifyForEndOfEngagement(equipmentStatus, openReturnStatus));
    }

    [Theory]
    // Nothing physically received yet.
    [InlineData(ReturnGroupCollectionStatus.Awaiting, new[] { ReturnStatus.Pending })]
    [InlineData(ReturnGroupCollectionStatus.Awaiting, new[] { ReturnStatus.Pending, ReturnStatus.Pending, ReturnStatus.Pending })]
    [InlineData(ReturnGroupCollectionStatus.Awaiting, new[] { ReturnStatus.Pending, ReturnStatus.Cancelled })]
    // Some received, some not.
    [InlineData(ReturnGroupCollectionStatus.Partial, new[] { ReturnStatus.Received, ReturnStatus.Pending, ReturnStatus.Pending })]
    [InlineData(ReturnGroupCollectionStatus.Partial, new[] { ReturnStatus.Completed, ReturnStatus.Pending })]
    [InlineData(ReturnGroupCollectionStatus.Partial, new[] { ReturnStatus.Received, ReturnStatus.Completed, ReturnStatus.Pending, ReturnStatus.Cancelled })]
    // Everything still expected has been received, whatever happened to the equipment afterwards.
    [InlineData(ReturnGroupCollectionStatus.Collected, new[] { ReturnStatus.Received })]
    [InlineData(ReturnGroupCollectionStatus.Collected, new[] { ReturnStatus.Received, ReturnStatus.Completed })]
    [InlineData(ReturnGroupCollectionStatus.Collected, new[] { ReturnStatus.Completed, ReturnStatus.Completed, ReturnStatus.Cancelled })]
    // Nothing left to collect.
    [InlineData(ReturnGroupCollectionStatus.Cancelled, new[] { ReturnStatus.Cancelled })]
    [InlineData(ReturnGroupCollectionStatus.Cancelled, new[] { ReturnStatus.Cancelled, ReturnStatus.Cancelled })]
    [InlineData(ReturnGroupCollectionStatus.Cancelled, new ReturnStatus[0])]
    public void DeriveGroupCollectionStatus_IsDerivedFromTheReturnsPhysicalReceiptOnly(ReturnGroupCollectionStatus expected, ReturnStatus[] statuses)
    {
        Assert.Equal(expected, ReturnRules.DeriveGroupCollectionStatus(statuses));
    }

    [Theory]
    // A single return: simply its own status.
    [InlineData(ReturnStatus.Pending, 1, 1, 0)]
    [InlineData(ReturnStatus.Received, 1, 0, 1)]
    [InlineData(ReturnStatus.Completed, 1, 0, 0)]
    [InlineData(ReturnStatus.Cancelled, 0, 0, 0)]
    // A group with nothing received (0/N) and one partly received (1/N): pending, and only pending.
    [InlineData(ReturnStatus.Pending, 3, 3, 0)]
    [InlineData(ReturnStatus.Pending, 3, 2, 1)]
    [InlineData(ReturnStatus.Pending, 3, 1, 0)]
    // Everything received (N/N): received while any inspection is open, completed once none is.
    [InlineData(ReturnStatus.Received, 3, 0, 3)]
    [InlineData(ReturnStatus.Received, 3, 0, 1)]
    [InlineData(ReturnStatus.Completed, 3, 0, 0)]
    public void DeriveListFilterStatus_PlacesARowUnderExactlyOneStatus(
        ReturnStatus expected, int notCancelledCount, int pendingCount, int receivedCount)
    {
        Assert.Equal(expected, ReturnRules.DeriveListFilterStatus(notCancelledCount, pendingCount, receivedCount));
    }

    [Fact]
    public void CanReceive_TrueOnlyForPending()
    {
        Assert.True(ReturnRules.CanReceive(ReturnStatus.Pending));
        Assert.False(ReturnRules.CanReceive(ReturnStatus.Received));
        Assert.False(ReturnRules.CanReceive(ReturnStatus.Completed));
        Assert.False(ReturnRules.CanReceive(ReturnStatus.Cancelled));
    }

    [Fact]
    public void CanCancel_TrueOnlyForPending()
    {
        Assert.True(ReturnRules.CanCancel(ReturnStatus.Pending));
        Assert.False(ReturnRules.CanCancel(ReturnStatus.Received));
        Assert.False(ReturnRules.CanCancel(ReturnStatus.Completed));
        Assert.False(ReturnRules.CanCancel(ReturnStatus.Cancelled));
    }

    [Fact]
    public void CanRecordInspection_TrueOnlyForReceived()
    {
        Assert.True(ReturnRules.CanRecordInspection(ReturnStatus.Received));
        Assert.False(ReturnRules.CanRecordInspection(ReturnStatus.Pending));
        Assert.False(ReturnRules.CanRecordInspection(ReturnStatus.Completed));
        Assert.False(ReturnRules.CanRecordInspection(ReturnStatus.Cancelled));
    }

    [Fact]
    public void CanFinalize_TrueOnlyForReceived()
    {
        Assert.True(ReturnRules.CanFinalize(ReturnStatus.Received));
        Assert.False(ReturnRules.CanFinalize(ReturnStatus.Pending));
        Assert.False(ReturnRules.CanFinalize(ReturnStatus.Completed));
        Assert.False(ReturnRules.CanFinalize(ReturnStatus.Cancelled));
    }

    [Theory]
    [InlineData(2026, 9, 8)]
    [InlineData(2026, 9, 7)]
    public void IsValidReceivedOn_TrueForTodayOrEarlier(int year, int month, int day)
    {
        var today = new DateOnly(2026, 9, 8);
        Assert.True(ReturnRules.IsValidReceivedOn(new DateOnly(year, month, day), today));
    }

    [Fact]
    public void IsValidReceivedOn_FalseWhenInTheFuture()
    {
        var today = new DateOnly(2026, 9, 8);
        Assert.False(ReturnRules.IsValidReceivedOn(new DateOnly(2026, 9, 9), today));
    }

    [Fact]
    public void RequiredReceivePhotoCount_IsThree()
    {
        Assert.Equal(3, ReturnRules.RequiredReceivePhotoCount);
    }

    [Theory]
    [InlineData(null, 0, false)]
    [InlineData("", 1, false)]
    [InlineData("  ", 1, false)]
    [InlineData("Cracked screen", 0, false)]
    [InlineData("Cracked screen", 1, true)]
    [InlineData("Cracked screen", 2, true)]
    public void HasRequiredDamageEvidence_NeedsDescriptionAndAtLeastOnePhoto(string? description, int photoCount, bool expected)
    {
        Assert.Equal(expected, ReturnRules.HasRequiredDamageEvidence(description, photoCount));
    }
}
