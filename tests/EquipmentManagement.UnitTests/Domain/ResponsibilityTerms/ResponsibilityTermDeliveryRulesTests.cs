using EquipmentManagement.Domain.ResponsibilityTerms;

namespace EquipmentManagement.UnitTests.Domain.ResponsibilityTerms;

public class ResponsibilityTermDeliveryRulesTests
{
    [Fact]
    public void CanSend_TrueOnlyForGenerated()
    {
        Assert.True(ResponsibilityTermDeliveryRules.CanSend(ResponsibilityTermStatus.Generated));
        Assert.False(ResponsibilityTermDeliveryRules.CanSend(ResponsibilityTermStatus.Superseded));
    }

    [Fact]
    public void CanUploadSignedArtifact_TrueOnlyWhenNoneExistsYet()
    {
        Assert.True(ResponsibilityTermDeliveryRules.CanUploadSignedArtifact(signedArtifactAlreadyExists: false));
        Assert.False(ResponsibilityTermDeliveryRules.CanUploadSignedArtifact(signedArtifactAlreadyExists: true));
    }

    [Theory]
    [InlineData("consultant@example.com", true)]
    [InlineData("first.last+tag@sub.example.test", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not-an-email", false)]
    [InlineData("missing-domain@", false)]
    [InlineData("@missing-local.com", false)]
    public void IsValidEmail_ValidatesSyntax(string email, bool expected)
    {
        Assert.Equal(expected, ResponsibilityTermDeliveryRules.IsValidEmail(email));
    }

    [Fact]
    public void DeriveExecutionStatus_SignedTakesPrecedenceOverSentAndAttempting()
    {
        Assert.Equal(
            ResponsibilityTermExecutionStatus.Signed,
            ResponsibilityTermDeliveryRules.DeriveExecutionStatus(hasSignedArtifact: true, hasSentDelivery: true, hasAttemptingDelivery: true));
    }

    [Fact]
    public void DeriveExecutionStatus_SentWhenNoSignatureButSentSuccessfully()
    {
        Assert.Equal(
            ResponsibilityTermExecutionStatus.Sent,
            ResponsibilityTermDeliveryRules.DeriveExecutionStatus(hasSignedArtifact: false, hasSentDelivery: true, hasAttemptingDelivery: false));
    }

    [Fact]
    public void DeriveExecutionStatus_SentTakesPrecedenceOverAttempting_WhenAnEarlierSendAlreadySucceeded()
    {
        // A concurrent/later resend still Attempting must not retract an
        // already-successful send (Phase 7 hardening pass).
        Assert.Equal(
            ResponsibilityTermExecutionStatus.Sent,
            ResponsibilityTermDeliveryRules.DeriveExecutionStatus(hasSignedArtifact: false, hasSentDelivery: true, hasAttemptingDelivery: true));
    }

    [Fact]
    public void DeriveExecutionStatus_SendPendingWhenOnlyAnUnresolvedAttemptExists()
    {
        Assert.Equal(
            ResponsibilityTermExecutionStatus.SendPending,
            ResponsibilityTermDeliveryRules.DeriveExecutionStatus(hasSignedArtifact: false, hasSentDelivery: false, hasAttemptingDelivery: true));
    }

    [Fact]
    public void DeriveExecutionStatus_NotSentWhenNeitherSentNorSignedNorAttempting()
    {
        Assert.Equal(
            ResponsibilityTermExecutionStatus.NotSent,
            ResponsibilityTermDeliveryRules.DeriveExecutionStatus(hasSignedArtifact: false, hasSentDelivery: false, hasAttemptingDelivery: false));
    }

    [Theory]
    [InlineData(DeliveryStatus.Attempting, DeliveryStatus.Sent, true)]
    [InlineData(DeliveryStatus.Attempting, DeliveryStatus.Failed, true)]
    [InlineData(DeliveryStatus.Attempting, DeliveryStatus.Attempting, false)]
    [InlineData(DeliveryStatus.Sent, DeliveryStatus.Failed, false)]
    [InlineData(DeliveryStatus.Failed, DeliveryStatus.Sent, false)]
    [InlineData(DeliveryStatus.Sent, DeliveryStatus.Attempting, false)]
    [InlineData(DeliveryStatus.Failed, DeliveryStatus.Attempting, false)]
    [InlineData(DeliveryStatus.Sent, DeliveryStatus.Sent, false)]
    [InlineData(DeliveryStatus.Failed, DeliveryStatus.Failed, false)]
    public void CanTransition_OnlyAttemptingToSentOrFailedIsLegal(DeliveryStatus from, DeliveryStatus to, bool expected)
    {
        Assert.Equal(expected, ResponsibilityTermDeliveryRules.CanTransition(from, to));
    }
}
