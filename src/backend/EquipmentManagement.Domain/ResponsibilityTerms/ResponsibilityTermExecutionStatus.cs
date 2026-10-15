using System.Diagnostics.CodeAnalysis;

namespace EquipmentManagement.Domain.ResponsibilityTerms;

/// <summary>
/// The delivery/signature workflow's status — deliberately separate from
/// <see cref="ResponsibilityTermStatus"/>, which continues to describe only
/// the document-version lifecycle (Generated/Superseded). Overloading
/// <see cref="ResponsibilityTermStatus"/> with delivery/signature concerns was
/// explicitly rejected (Phase 7 scope). This is a derived value,
/// never a stored/mutable column — see <c>ResponsibilityTermDeliveryRules.DeriveExecutionStatus</c> —
/// computed from whether any <see cref="ResponsibilityTermDelivery"/>/
/// <see cref="ResponsibilityTermSignedArtifact"/> rows exist, so it can never
/// drift from its source rows.
///
/// Deliberately minimal (Phase 7 scope): no
/// Viewed/Opened/Declined/Expired/ReminderSent/IdentityVerified value exists,
/// because no mechanism in this codebase can truthfully produce any of them.
///
/// <see cref="SendPending"/> was added in the Phase 7 hardening pass
/// (ADR-052 update) to truthfully represent a delivery attempt that is
/// durably recorded as <see cref="ResponsibilityTermDelivery.Status"/> ==
/// <see cref="DeliveryStatus.Attempting"/> with no successful send yet on
/// record — normally near-instantaneous, but a real, honest state after a
/// crash/outage between a mail-transport call and its terminal DB write.
/// </summary>
public enum ResponsibilityTermExecutionStatus
{
    NotSent,

    /// <summary>An attempt was durably initiated and has not (yet, or ever) reached a terminal Sent/Failed outcome. Never reported merely because an attempt exists if a prior attempt already succeeded — see <c>ResponsibilityTermDeliveryRules.DeriveExecutionStatus</c>.</summary>
    SendPending,

    Sent,

    /// <summary>Not a reference to the C-style "signed" numeric keyword — CA1720's false positive here is suppressed rather than picking a less accurate name.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "\"Signed\" is the correct domain term (Phase 7 scope) — not a numeric-type reference.")]
    Signed,
}
