namespace EquipmentManagement.Domain.Auditing;

/// <summary>
/// An immutable record of a security- or business-relevant event
/// (docs/PROJECT_SPEC.md, section 15). Append-only from the application's
/// perspective: nothing in this codebase updates or deletes a row after insert —
/// see Infrastructure's AuditService and the read-only AuditController.
/// </summary>
/// <remarks>
/// A plain POCO with no EF Core/Npgsql attributes: Domain has zero dependencies,
/// so all persistence mapping (table name, JSONB columns, indexes) lives in
/// Infrastructure's <c>ApplicationDbContext.OnModelCreating</c>.
/// </remarks>
public sealed class AuditLog
{
    public Guid Id { get; set; }

    /// <summary>
    /// Null for system/bootstrap actions with no human actor (e.g.
    /// <see cref="AuditActions.System.AdministrationUserBootstrapped"/>).
    /// </summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// A snapshot of the actor's display name at the time of the event, so the
    /// audit trail stays readable even if that user's name is later changed or
    /// their account is deactivated (docs/PROJECT_SPEC.md, section 15).
    /// </summary>
    public string? ActorDisplayNameSnapshot { get; set; }

    /// <summary>One of the constants in <see cref="AuditActions"/>.</summary>
    public required string Action { get; set; }

    /// <summary>The kind of entity affected, e.g. "User". Free text, not an FK.</summary>
    public required string EntityType { get; set; }

    /// <summary>
    /// The affected entity's identifier as a string (entities use different key
    /// types across modules). Null when the event has no single target entity yet
    /// (e.g. a failed login for an email with no matching account).
    /// </summary>
    public string? EntityId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string? CorrelationId { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>
    /// Safe-field-only JSON snapshot of the entity before the change. Never the
    /// full persistence entity — see Infrastructure's explicit snapshot builders.
    /// </summary>
    public string? OldValuesJson { get; set; }

    /// <summary>Safe-field-only JSON snapshot of the entity after the change.</summary>
    public string? NewValuesJson { get; set; }

    /// <summary>Additional safe, structured context specific to the action.</summary>
    public string? MetadataJson { get; set; }
}
