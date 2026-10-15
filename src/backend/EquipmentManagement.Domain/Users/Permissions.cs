namespace EquipmentManagement.Domain.Users;

/// <summary>
/// Canonical permission identifiers used across authorization policies, role
/// assignments and (eventually) the user-management UI. Each value doubles as
/// the ASP.NET Core authorization policy name for that permission.
/// </summary>
/// <remarks>
/// <c>User.*</c>, <c>Audit.Read</c> (Phase 1), <c>Company.*</c>,
/// <c>Consultant.*</c>, <c>ConsultantAllocation.*</c> (Phase 2),
/// <c>EquipmentType.*</c>, <c>Equipment.Read/Create/Update/Retire</c> (Phase 3),
/// <c>Equipment.Assign</c>, <c>EquipmentHandover.*</c>,
/// <c>EquipmentAssignment.Read</c> (Phase 4), <c>Equipment.Return</c>,
/// <c>EquipmentReturn.*</c>, <c>EquipmentMaintenance.*</c> (Phase 5),
/// <c>ResponsibilityTerm.Read/Generate</c> (Phase 6), and
/// <c>ResponsibilityTerm.Send/SignatureManage</c> (Phase 7) are implemented
/// and mapped to roles (see <see cref="RolePermissions"/>).
/// </remarks>
public static class Permissions
{
    public static class User
    {
        public const string Read = "User.Read";
        public const string Create = "User.Create";
        public const string Update = "User.Update";
        public const string Archive = "User.Archive";
        public const string AssignRole = "User.AssignRole";
    }

    public static class Company
    {
        public const string Read = "Company.Read";
        public const string Create = "Company.Create";
        public const string Update = "Company.Update";
        public const string Archive = "Company.Archive";
    }

    public static class Consultant
    {
        public const string Read = "Consultant.Read";
        public const string Create = "Consultant.Create";
        public const string Update = "Consultant.Update";
        public const string Archive = "Consultant.Archive";

        /// <summary>Covers template download, preview and confirmation (Phase 8 scope) — no separate template-only permission.</summary>
        public const string Import = "Consultant.Import";

        public const string Export = "Consultant.Export";
    }

    public static class ConsultantAllocation
    {
        public const string Read = "ConsultantAllocation.Read";
        public const string Create = "ConsultantAllocation.Create";
        public const string Update = "ConsultantAllocation.Update";
        public const string End = "ConsultantAllocation.End";
    }

    public static class EquipmentType
    {
        public const string Read = "EquipmentType.Read";
        public const string Create = "EquipmentType.Create";
        public const string Update = "EquipmentType.Update";
        public const string Archive = "EquipmentType.Archive";
    }

    public static class Equipment
    {
        public const string Read = "Equipment.Read";
        public const string Create = "Equipment.Create";
        public const string Update = "Equipment.Update";
        public const string Retire = "Equipment.Retire";
        public const string Assign = "Equipment.Assign";
        public const string Return = "Equipment.Return";

        public const string Import = "Equipment.Import";

        public const string Export = "Equipment.Export";

        /// <summary>Covers establishing the first current condition photo set — at creation or via the legacy backfill action for equipment that predates this feature. Distinct from <see cref="Update"/> since ordinary equipment edits must never touch photos.</summary>
        public const string PhotosManage = "Equipment.PhotosManage";

        /// <summary>Administrative correction of the CURRENT condition photo set ("Alterar fotografias") outside custody — Administration-only, deliberately distinct from <see cref="PhotosManage"/>, which HR also holds for normal photo registration.</summary>
        public const string PhotosReplace = "Equipment.PhotosReplace";
    }

    public static class EquipmentHandover
    {
        public const string Read = "EquipmentHandover.Read";
        public const string Create = "EquipmentHandover.Create";
        public const string Update = "EquipmentHandover.Update";

        /// <summary>Registering that equipment has been physically shipped ("Registar envio") — a Draft-only operation, distinct from <see cref="Update"/>, that also sends the delivery-estimate notification email.</summary>
        public const string Dispatch = "EquipmentHandover.Dispatch";

        /// <summary>Cancelling a registered carrier dispatch before delivery is confirmed ("Anular envio"): the shipped equipment returns from InTransit to Available, with a mandatory reason. Distinct from <see cref="Dispatch"/> and <see cref="Update"/>.</summary>
        public const string CancelDispatch = "EquipmentHandover.CancelDispatch";
    }

    public static class EquipmentAssignment
    {
        public const string Read = "EquipmentAssignment.Read";
    }

    public static class EquipmentReturn
    {
        public const string Read = "EquipmentReturn.Read";
        public const string Update = "EquipmentReturn.Update";
    }

    public static class EquipmentMaintenance
    {
        public const string Read = "EquipmentMaintenance.Read";
        public const string Manage = "EquipmentMaintenance.Manage";
    }

    /// <summary>
    /// HR requests; Administration reviews/decides. <see cref="Create"/> is the
    /// only capability HR needs — it does not grant HR the ability to execute
    /// the actual retirement (still gated by <see cref="Equipment.Retire"/>,
    /// Administration-only). <see cref="Read"/>/<see cref="Decide"/> back the
    /// Administration-only "Solicitações de abate" review area.
    /// </summary>
    public static class EquipmentRetirementRequest
    {
        public const string Create = "EquipmentRetirementRequest.Create";
        public const string Read = "EquipmentRetirementRequest.Read";
        public const string Decide = "EquipmentRetirementRequest.Decide";
    }

    public static class ResponsibilityTerm
    {
        public const string Read = "ResponsibilityTerm.Read";

        /// <summary>Covers both first generation and regeneration — the same operation, so no separate Regenerate permission (Phase 6 scope).</summary>
        public const string Generate = "ResponsibilityTerm.Generate";

        /// <summary>Covers both the first send and any resend — the same operation (Phase 7 scope).</summary>
        public const string Send = "ResponsibilityTerm.Send";

        /// <summary>Covers uploading and downloading the signed artifact (Phase 7 scope).</summary>
        public const string SignatureManage = "ResponsibilityTerm.SignatureManage";
    }

    public static class Audit
    {
        public const string Read = "Audit.Read";
    }

    /// <summary>
    /// Every known permission, used to register one authorization policy per
    /// permission at startup. Keep in sync with the nested classes above.
    /// </summary>
    public static IReadOnlyCollection<string> All { get; } =
    [
        User.Read,
        User.Create,
        User.Update,
        User.Archive,
        User.AssignRole,
        Company.Read,
        Company.Create,
        Company.Update,
        Company.Archive,
        Consultant.Read,
        Consultant.Create,
        Consultant.Update,
        Consultant.Archive,
        Consultant.Import,
        Consultant.Export,
        ConsultantAllocation.Read,
        ConsultantAllocation.Create,
        ConsultantAllocation.Update,
        ConsultantAllocation.End,
        EquipmentType.Read,
        EquipmentType.Create,
        EquipmentType.Update,
        EquipmentType.Archive,
        Equipment.Read,
        Equipment.Create,
        Equipment.Update,
        Equipment.Retire,
        Equipment.Assign,
        Equipment.Return,
        Equipment.Import,
        Equipment.Export,
        Equipment.PhotosManage,
        Equipment.PhotosReplace,
        EquipmentHandover.Read,
        EquipmentHandover.Create,
        EquipmentHandover.Update,
        EquipmentHandover.Dispatch,
        EquipmentHandover.CancelDispatch,
        EquipmentAssignment.Read,
        EquipmentReturn.Read,
        EquipmentReturn.Update,
        EquipmentMaintenance.Read,
        EquipmentMaintenance.Manage,
        EquipmentRetirementRequest.Create,
        EquipmentRetirementRequest.Read,
        EquipmentRetirementRequest.Decide,
        ResponsibilityTerm.Read,
        ResponsibilityTerm.Generate,
        ResponsibilityTerm.Send,
        ResponsibilityTerm.SignatureManage,
        Audit.Read,
    ];
}
