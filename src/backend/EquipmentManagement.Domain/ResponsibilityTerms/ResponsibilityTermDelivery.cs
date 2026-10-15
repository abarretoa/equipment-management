namespace EquipmentManagement.Domain.ResponsibilityTerms;

/// <summary>
/// One email-send attempt for a <see cref="ResponsibilityTerm"/> (Phase 7 scope; Phase 7 hardening pass, ADR-052 update). A term
/// may be resent any number of times; each attempt gets its own row —
/// nothing here is ever overwritten or deleted, so "sent 10/09, sent again
/// 12/09" both remain permanently visible.
///
/// A row is inserted as <see cref="DeliveryStatus.Attempting"/> — durably
/// committed — *before* the email is handed to the mail transport, and is
/// updated exactly once afterward to its terminal <see cref="DeliveryStatus.Sent"/>
/// or <see cref="DeliveryStatus.Failed"/> state (see
/// <c>ResponsibilityTermDeliveryService.SendAsync</c> and
/// <c>Domain.ResponsibilityTerms.ResponsibilityTermDeliveryRules.CanTransition</c>).
/// This is what guarantees "no email side effect without a durable attempt
/// record": if the attempt row cannot be committed, the email is never sent.
/// A row can remain <see cref="DeliveryStatus.Attempting"/> indefinitely if
/// the application crashes or loses its database connection between a
/// successful mail-transport call and the terminal update — this is an
/// accepted, truthfully-represented ambiguity (the application does not know
/// whether the email went out), not a bug to paper over by guessing.
/// </summary>
public sealed class ResponsibilityTermDelivery
{
    public Guid Id { get; set; }

    public Guid ResponsibilityTermId { get; set; }

    /// <summary>
    /// The exact address this attempt targeted — the historical Consultant
    /// snapshot email by default, or an HR-supplied override (Phase 7 scope). Never the Consultant's current, possibly-since-changed
    /// email.
    /// </summary>
    public required string RecipientEmailSnapshot { get; set; }

    /// <summary>True when <see cref="RecipientEmailSnapshot"/> was an explicit HR-supplied override rather than the default historical Consultant snapshot email.</summary>
    public bool RecipientOverridden { get; set; }

    public DeliveryStatus Status { get; set; }

    public DateTime AttemptedAtUtc { get; set; }

    /// <summary>Set only when <see cref="Status"/> is <see cref="DeliveryStatus.Sent"/> — the moment the configured mail provider accepted the message, never proof the recipient received or read it (Phase 7 scope).</summary>
    public DateTime? SentAtUtc { get; set; }

    /// <summary>A safe, closed classification only — never a raw SMTP/provider exception message, which could carry secrets or PII (Phase 7 scope).</summary>
    public DeliveryFailureCode? FailureCode { get; set; }

    public Guid SentByUserId { get; set; }

    public required string SentByDisplayNameSnapshot { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Deliberately narrow (Phase 7 scope): the application can only
/// truthfully know whether the configured mail provider accepted the send
/// attempt, never whether the recipient received or read the message — so
/// there is no <c>Delivered</c>/<c>Read</c>/<c>Viewed</c> value.
/// </summary>
public enum DeliveryStatus
{
    /// <summary>
    /// A send operation was durably initiated but its final outcome is not
    /// yet — or, in the rare crash/outage case, may never be — durably known
    /// by the application. The only legal transitions out of this state are
    /// to <see cref="Sent"/> or <see cref="Failed"/>, each exactly once.
    /// </summary>
    Attempting,

    Sent,
    Failed,
}

/// <summary>Safe failure classification (Phase 7 scope) — never the underlying provider exception, which could contain secrets or PII.</summary>
public enum DeliveryFailureCode
{
    ConfigurationError,
    ProviderRejected,
    TemporaryFailure,
    Unknown,
}
