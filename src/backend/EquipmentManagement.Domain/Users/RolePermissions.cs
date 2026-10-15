namespace EquipmentManagement.Domain.Users;

/// <summary>
/// The single source of truth for which permissions each role carries. Consumed by
/// <c>IdentitySeeder</c> (Infrastructure) to sync role claims on startup, and by
/// unit tests that verify the Administration/HR access boundary directly, without
/// a database. Changing access for a role is a one-line change here, not a
/// per-controller edit (docs/PROJECT_SPEC.md, section 4).
/// </summary>
public static class RolePermissions
{
    /// <summary>
    /// Administration is the full-access role: user management, audit, and
    /// every Phase 2/3/4/5/6/7 business permission, including finalizing a
    /// handover (<c>Equipment.Assign</c>), the full return/damage/maintenance
    /// workflow (<c>Equipment.Return</c>, <c>EquipmentReturn.*</c>,
    /// <c>EquipmentMaintenance.*</c>), Responsibility Term generation, and the
    /// Phase 7 email-delivery/signed-document workflow (<c>ResponsibilityTerm.*</c>).
    /// Administration alone retires equipment (<c>Equipment.Retire</c>), alone
    /// corrects an equipment's current condition photos (<c>Equipment.PhotosReplace</c>), and
    /// alone reviews/decides HR's retirement requests
    /// (<c>EquipmentRetirementRequest.Read/Decide</c>) — it does not need
    /// <c>EquipmentRetirementRequest.Create</c> since it never needs to
    /// *request* what it can already do directly.
    /// </summary>
    private static readonly IReadOnlyCollection<string> AdministrationPermissions =
    [
        Permissions.User.Read,
        Permissions.User.Create,
        Permissions.User.Update,
        Permissions.User.Archive,
        Permissions.User.AssignRole,
        Permissions.Company.Read,
        Permissions.Company.Create,
        Permissions.Company.Update,
        Permissions.Company.Archive,
        Permissions.Consultant.Read,
        Permissions.Consultant.Create,
        Permissions.Consultant.Update,
        Permissions.Consultant.Archive,
        Permissions.Consultant.Import,
        Permissions.Consultant.Export,
        Permissions.ConsultantAllocation.Read,
        Permissions.ConsultantAllocation.Create,
        Permissions.ConsultantAllocation.Update,
        Permissions.ConsultantAllocation.End,
        Permissions.EquipmentType.Read,
        Permissions.EquipmentType.Create,
        Permissions.EquipmentType.Update,
        Permissions.EquipmentType.Archive,
        Permissions.Equipment.Read,
        Permissions.Equipment.Create,
        Permissions.Equipment.Update,
        Permissions.Equipment.Retire,
        Permissions.Equipment.Assign,
        Permissions.Equipment.Return,
        Permissions.Equipment.Import,
        Permissions.Equipment.Export,
        Permissions.Equipment.PhotosManage,
        Permissions.Equipment.PhotosReplace,
        Permissions.EquipmentHandover.Read,
        Permissions.EquipmentHandover.Create,
        Permissions.EquipmentHandover.Update,
        Permissions.EquipmentHandover.Dispatch,
        Permissions.EquipmentHandover.CancelDispatch,
        Permissions.EquipmentAssignment.Read,
        Permissions.EquipmentReturn.Read,
        Permissions.EquipmentReturn.Update,
        Permissions.EquipmentMaintenance.Read,
        Permissions.EquipmentMaintenance.Manage,
        Permissions.EquipmentRetirementRequest.Read,
        Permissions.EquipmentRetirementRequest.Decide,
        Permissions.ResponsibilityTerm.Read,
        Permissions.ResponsibilityTerm.Generate,
        Permissions.ResponsibilityTerm.Send,
        Permissions.ResponsibilityTerm.SignatureManage,
        Permissions.Audit.Read,
    ];

    /// <summary>
    /// HR still cannot manage internal users or read the audit log
    /// (docs/PROJECT_SPEC.md, section 15: audit is Administration-only) — that
    /// boundary is unchanged by Phase 2/3/4. HR gets full operational Consultant/
    /// ConsultantAllocation access (docs/PROJECT_SPEC.md, section 3: RH manages
    /// consultants), can create/update (not archive) Companies (the approved
    /// Phase 2 permission adjustment), and — mirroring the exact same
    /// create/update-vs-archive split — can create/update/read EquipmentTypes
    /// and Equipment but not archive an EquipmentType or retire Equipment,
    /// both of which remove something from normal operational circulation and
    /// stay Administration-only. HR performs the entire Phase 4 handover
    /// workflow end to end, including finalization (docs/PROJECT_SPEC.md,
    /// section 11: "O RH deverá poder clicar em 'Gerar Termo'..." — HR is the
    /// role that actually runs deliveries in practice) — so it gets
    /// <c>Equipment.Assign</c> and the full <c>EquipmentHandover.*</c>/
    /// <c>EquipmentAssignment.Read</c> set, same as Administration. HR also
    /// operationally owns the entire Phase 5 return/damage/maintenance
    /// workflow end to end (Phase 5 scope: "RH handles receiving
    /// returned equipment and attaching photos" — extended, per explicit
    /// product decision, to the whole workflow rather than just photos) — so
    /// it gets <c>Equipment.Return</c>, <c>EquipmentReturn.*</c> and
    /// <c>EquipmentMaintenance.*</c>, identical to Administration. HR also
    /// runs the entire Phase 7 delivery/signature workflow identically to
    /// Administration (<c>ResponsibilityTerm.Send</c>,
    /// <c>ResponsibilityTerm.SignatureManage</c>) — same shape as every prior
    /// phase's grant (ADR-034). HR also runs Consultant/Equipment Excel
    /// import-export identically to Administration (<c>Consultant.Import</c>/
    /// <c>Export</c>, <c>Equipment.Import</c>/<c>Export</c>) — normal HR
    /// operational work (Phase 8 scope). HR does NOT retire
    /// equipment directly (<c>Equipment.Retire</c> stays Administration-only,
    /// same split as archiving) — instead it gets
    /// <c>EquipmentRetirementRequest.Create</c> only, to request that
    /// Administration retire eligible equipment; it gets neither
    /// <c>Read</c> nor <c>Decide</c> for that workflow, since reviewing and
    /// deciding those requests is Administration's job. See ADR-080.
    /// </summary>
    private static readonly IReadOnlyCollection<string> HrPermissions =
    [
        Permissions.Company.Read,
        Permissions.Company.Create,
        Permissions.Company.Update,
        Permissions.Consultant.Read,
        Permissions.Consultant.Create,
        Permissions.Consultant.Update,
        Permissions.Consultant.Archive,
        Permissions.Consultant.Import,
        Permissions.Consultant.Export,
        Permissions.ConsultantAllocation.Read,
        Permissions.ConsultantAllocation.Create,
        Permissions.ConsultantAllocation.Update,
        Permissions.ConsultantAllocation.End,
        Permissions.EquipmentType.Read,
        Permissions.EquipmentType.Create,
        Permissions.EquipmentType.Update,
        Permissions.Equipment.Read,
        Permissions.Equipment.Create,
        Permissions.Equipment.Update,
        Permissions.Equipment.Assign,
        Permissions.Equipment.Return,
        Permissions.Equipment.Import,
        Permissions.Equipment.Export,
        Permissions.Equipment.PhotosManage,
        Permissions.EquipmentHandover.Read,
        Permissions.EquipmentHandover.Create,
        Permissions.EquipmentHandover.Update,
        Permissions.EquipmentHandover.Dispatch,
        Permissions.EquipmentHandover.CancelDispatch,
        Permissions.EquipmentAssignment.Read,
        Permissions.EquipmentReturn.Read,
        Permissions.EquipmentReturn.Update,
        Permissions.EquipmentMaintenance.Read,
        Permissions.EquipmentMaintenance.Manage,
        Permissions.EquipmentRetirementRequest.Create,
        Permissions.ResponsibilityTerm.Read,
        Permissions.ResponsibilityTerm.Generate,
        Permissions.ResponsibilityTerm.Send,
        Permissions.ResponsibilityTerm.SignatureManage,
    ];

    private static readonly Dictionary<string, IReadOnlyCollection<string>> Map =
        new(StringComparer.Ordinal)
        {
            [Roles.Administration] = AdministrationPermissions,
            [Roles.HR] = HrPermissions,
        };

    public static IReadOnlyCollection<string> GetPermissions(string role) =>
        Map.TryGetValue(role, out var permissions) ? permissions : [];

    /// <summary>
    /// <see cref="PermissionMapFingerprint"/> of this map, computed once per
    /// process (the map is immutable). Declared after <see cref="Map"/> so the
    /// static initializer sees it populated.
    /// </summary>
    public static string Fingerprint { get; } = PermissionMapFingerprint.Compute(
        Map.ToDictionary(entry => entry.Key, entry => (IEnumerable<string>)entry.Value, StringComparer.Ordinal));
}
