using System.Net.Mail;

namespace EquipmentManagement.Domain.ResponsibilityTerms;

/// <summary>
/// Pure business rules for the Phase 7 delivery/signature workflow (Phase 7 scope). Framework- and database-free — <c>Infrastructure.ResponsibilityTerms.ResponsibilityTermDeliveryService</c>
/// and <c>ResponsibilityTermSignedArtifactService</c> supply the actual rows
/// to check these against.
/// </summary>
public static class ResponsibilityTermDeliveryRules
{
    /// <summary>
    /// Only the current (<see cref="ResponsibilityTermStatus.Generated"/>)
    /// version may start a new send — a <see cref="ResponsibilityTermStatus.Superseded"/>
    /// term cannot start a new normal send workflow (Phase 7 scope). Existing delivery history for a superseded term remains readable
    /// regardless.
    /// </summary>
    public static bool CanSend(ResponsibilityTermStatus status) => status == ResponsibilityTermStatus.Generated;

    /// <summary>
    /// A signed artifact may be uploaded against any existing term — current
    /// or superseded (Phase 7 scope: a signed copy of a superseded
    /// version returned late is still truthful historical evidence and must
    /// not be silently attached to a newer version, or rejected outright).
    /// Eligibility is "does this exact term row exist," never "is it current."
    /// </summary>
    public static bool CanUploadSignedArtifact(bool signedArtifactAlreadyExists) => !signedArtifactAlreadyExists;

    /// <summary>Uses <see cref="MailAddress"/>'s own RFC-2822 parsing rather than a bespoke regex — the same validation .NET's own libraries rely on.</summary>
    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// The only two legal delivery-attempt transitions (Phase 7 hardening
    /// pass, ADR-052 update): a durably-recorded
    /// <see cref="DeliveryStatus.Attempting"/> row may resolve to
    /// <see cref="DeliveryStatus.Sent"/> or <see cref="DeliveryStatus.Failed"/>,
    /// exactly once. Nothing may leave <see cref="DeliveryStatus.Sent"/> or
    /// <see cref="DeliveryStatus.Failed"/> once reached — no
    /// <c>Sent -&gt; Failed</c>, <c>Failed -&gt; Sent</c>, or
    /// <c>Sent/Failed -&gt; Attempting</c>. <c>ResponsibilityTermDeliveryService.SendAsync</c>
    /// asserts this before every terminal write, as a self-check rather than
    /// a trust-the-caller convention.
    /// </summary>
    public static bool CanTransition(DeliveryStatus from, DeliveryStatus to) =>
        from == DeliveryStatus.Attempting && to is DeliveryStatus.Sent or DeliveryStatus.Failed;

    /// <summary>
    /// A signed artifact existing is the strongest signal (Signed implies the
    /// term was necessarily sent out-of-band at some point, even if this
    /// system has no delivery row for it — e.g. a term shared by another
    /// channel and returned signed) — it takes precedence even over an
    /// unresolved <paramref name="hasAttemptingDelivery"/>. Otherwise, any
    /// successful send makes it Sent — a lingering unresolved attempt from a
    /// separate resend never retracts an already-successful one. With no
    /// successful send at all, an unresolved <see cref="DeliveryStatus.Attempting"/>
    /// row reports <see cref="ResponsibilityTermExecutionStatus.SendPending"/>
    /// (never <c>Sent</c> — Phase 7 hardening pass); with neither, it is NotSent.
    /// </summary>
    public static ResponsibilityTermExecutionStatus DeriveExecutionStatus(
        bool hasSignedArtifact, bool hasSentDelivery, bool hasAttemptingDelivery)
    {
        if (hasSignedArtifact)
        {
            return ResponsibilityTermExecutionStatus.Signed;
        }

        if (hasSentDelivery)
        {
            return ResponsibilityTermExecutionStatus.Sent;
        }

        return hasAttemptingDelivery ? ResponsibilityTermExecutionStatus.SendPending : ResponsibilityTermExecutionStatus.NotSent;
    }
}
