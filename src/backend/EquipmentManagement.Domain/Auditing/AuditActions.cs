namespace EquipmentManagement.Domain.Auditing;

/// <summary>
/// Canonical audit action identifiers (docs/PROJECT_SPEC.md, section 15; Phase 1 scope). Keeping these as constants (not a free-text string at each
/// call site) prevents typo'd/inconsistent action names from fragmenting the audit
/// trail, and gives the audit query API a closed, discoverable filter vocabulary.
/// </summary>
public static class AuditActions
{
    public static class Auth
    {
        public const string LoginSucceeded = "Auth.LoginSucceeded";
        public const string LoginFailed = "Auth.LoginFailed";
        public const string Logout = "Auth.Logout";
        public const string PasswordChanged = "Auth.PasswordChanged";
        public const string AdminPasswordResetRequested = "Auth.AdminPasswordResetRequested";
    }

    public static class User
    {
        public const string Created = "User.Created";
        public const string Updated = "User.Updated";
        public const string Activated = "User.Activated";
        public const string Deactivated = "User.Deactivated";
        public const string RoleChanged = "User.RoleChanged";
        public const string ProfileUpdated = "User.ProfileUpdated";
    }

    public static class Company
    {
        public const string Created = "Company.Created";
        public const string Updated = "Company.Updated";
        public const string Activated = "Company.Activated";
        public const string Deactivated = "Company.Deactivated";
    }

    public static class Consultant
    {
        public const string Created = "Consultant.Created";
        public const string Updated = "Consultant.Updated";
        public const string Activated = "Consultant.Activated";
        public const string Deactivated = "Consultant.Deactivated";
    }

    public static class ConsultantAllocation
    {
        public const string Created = "ConsultantAllocation.Created";
        public const string Updated = "ConsultantAllocation.Updated";

        /// <summary>Payload now also carries <c>IsEndOfEngagement</c> — a safe boolean fact, not a free-text reason.</summary>
        public const string Ended = "ConsultantAllocation.Ended";

        /// <summary>Recorded only for an end-of-engagement ending with active equipment custody — the collection window HR provided.</summary>
        public const string CollectionEstimateRecorded = "ConsultantAllocation.CollectionEstimateRecorded";

        /// <summary>
        /// Recorded only for an end-of-engagement ending with active equipment
        /// custody — the ids of the returns it initiated and of those already
        /// pending that it left untouched. Each initiated return also keeps its
        /// own <see cref="EquipmentReturn.Created"/> entry.
        /// </summary>
        public const string EndOfEngagementReturnsInitiated = "ConsultantAllocation.EndOfEngagementReturnsInitiated";

        /// <summary>Recorded with safe metadata only — never the recipient email itself.</summary>
        public const string CollectionNotificationSent = "ConsultantAllocation.CollectionNotificationSent";

        /// <summary>Recorded with a safe failure classification only — never the raw provider exception.</summary>
        public const string CollectionNotificationFailed = "ConsultantAllocation.CollectionNotificationFailed";
    }

    public static class EquipmentType
    {
        public const string Created = "EquipmentType.Created";
        public const string Updated = "EquipmentType.Updated";
        public const string Activated = "EquipmentType.Activated";
        public const string Deactivated = "EquipmentType.Deactivated";
    }

    public static class Equipment
    {
        public const string Created = "Equipment.Created";
        public const string Updated = "Equipment.Updated";
        public const string Retired = "Equipment.Retired";
    }

    public static class EquipmentPhotoSet
    {
        /// <summary>Recorded when the first current photo set is created for a Laptop/MobilePhone — at creation or via the legacy backfill action. Never includes file bytes.</summary>
        public const string Established = "EquipmentPhotoSet.Established";

        /// <summary>Recorded when Return physical receipt supersedes the previous current set with a new one.</summary>
        public const string Updated = "EquipmentPhotoSet.Updated";

        /// <summary>Recorded when Administration replaces ("Alterar fotografias") the current set with a corrected one outside custody. Ids, asset tag and replaced slot numbers only — no file bytes or names.</summary>
        public const string Replaced = "EquipmentPhotoSet.Replaced";
    }

    public static class EquipmentRetirementRequest
    {
        public const string Created = "EquipmentRetirementRequest.Created";
        public const string Approved = "EquipmentRetirementRequest.Approved";

        /// <summary>Recorded with safe metadata only — never the free-text rejection reason itself, same discipline as <see cref="EquipmentReturn.Cancelled"/>'s cancellation reason.</summary>
        public const string Rejected = "EquipmentRetirementRequest.Rejected";
    }

    public static class EquipmentHandover
    {
        public const string Created = "EquipmentHandover.Created";
        public const string ItemAdded = "EquipmentHandover.ItemAdded";
        public const string ItemRemoved = "EquipmentHandover.ItemRemoved";
        public const string InspectionRecorded = "EquipmentHandover.InspectionRecorded";
        public const string PhotoUploaded = "EquipmentHandover.PhotoUploaded";
        public const string PhotoDeleted = "EquipmentHandover.PhotoDeleted";
        public const string Finalized = "EquipmentHandover.Finalized";

        /// <summary>Recorded with the old and new method names only (Iteration 3).</summary>
        public const string DeliveryMethodChanged = "EquipmentHandover.DeliveryMethodChanged";

        /// <summary>An abandoned Draft removed before its operational lock. Recorded with ids, method and asset tags only — no consultant PII.</summary>
        public const string DraftDeleted = "EquipmentHandover.DraftDeleted";
    }

    public static class EquipmentDispatch
    {
        /// <summary>Recorded with safe metadata only (dispatchId, handoverId, dispatched items' asset tags/type names, consultantId/display name, expected delivery window) — this payload doubles as the minimal immutable record of what was communicated to the Consultant.</summary>
        public const string Registered = "EquipmentDispatch.Registered";

        /// <summary>Recorded with safe metadata only — never the recipient email itself, same discipline as <see cref="ResponsibilityTerm.Sent"/>.</summary>
        public const string NotificationSent = "EquipmentDispatch.NotificationSent";

        /// <summary>Recorded with a safe failure classification only — never the raw provider exception.</summary>
        public const string NotificationFailed = "EquipmentDispatch.NotificationFailed";

        /// <summary>"Anular envio": the active dispatch cancelled before delivery was confirmed. Records the dispatch, the reason, the asset tags and their InTransit -> Available transition, and any current term it superseded — never Consultant PII.</summary>
        public const string Cancelled = "EquipmentDispatch.Cancelled";
    }

    public static class EquipmentAssignment
    {
        public const string Started = "EquipmentAssignment.Started";
        public const string Ended = "EquipmentAssignment.Ended";
    }

    public static class EquipmentReturn
    {
        public const string Created = "EquipmentReturn.Created";
        public const string Received = "EquipmentReturn.Received";
        public const string Completed = "EquipmentReturn.Completed";

        /// <summary>
        /// Recorded with safe metadata only (returnId, equipmentId, assetTag,
        /// assignmentId) — the free-text cancellation reason is deliberately
        /// never copied into the audit trail (Phase 5 cancellation amendment).
        /// </summary>
        public const string Cancelled = "EquipmentReturn.Cancelled";
    }

    public static class EquipmentReturnGroup
    {
        /// <summary>
        /// Recorded once per return-initiation event (a manual return of one
        /// or more items, or an end of engagement) with the consultant id, the
        /// ids of the returns it created and, for an end of engagement, the
        /// allocation id. Each return still has its own
        /// <see cref="EquipmentReturn.Created"/> entry.
        /// </summary>
        public const string Created = "EquipmentReturnGroup.Created";
    }

    public static class ReturnInspection
    {
        public const string Recorded = "ReturnInspection.Recorded";
        public const string PhotoAdded = "ReturnInspection.PhotoAdded";
        public const string PhotoRemoved = "ReturnInspection.PhotoRemoved";
        public const string Finalized = "ReturnInspection.Finalized";
    }

    public static class Damage
    {
        public const string Recorded = "Damage.Recorded";
        public const string PhotoAdded = "Damage.PhotoAdded";
        public const string PhotoRemoved = "Damage.PhotoRemoved";
        public const string Resolved = "Damage.Resolved";
    }

    public static class Maintenance
    {
        public const string Started = "Maintenance.Started";
        public const string Completed = "Maintenance.Completed";
    }

    public static class ResponsibilityTerm
    {
        /// <summary>Recorded for every new version — v1 and every regeneration alike; no separate "Regenerated" action (Phase 6 scope's suggested vocabulary consolidated to avoid redundant near-duplicate actions).</summary>
        public const string Generated = "ResponsibilityTerm.Generated";

        /// <summary>Recorded alongside <see cref="Generated"/> only when a prior current version is replaced.</summary>
        public const string Superseded = "ResponsibilityTerm.Superseded";

        /// <summary>
        /// Deliberately audited: the document contains Consultant PII, so
        /// knowing who downloaded it (not just who generated it) is
        /// operationally useful (Phase 6 scope).
        /// </summary>
        public const string Downloaded = "ResponsibilityTerm.Downloaded";

        /// <summary>Recorded with safe metadata only (termId, documentNumber, version, deliveryAttemptId, recipientOverridden) — never the recipient email itself (Phase 7 scope).</summary>
        public const string Sent = "ResponsibilityTerm.Sent";

        /// <summary>Recorded with a safe failure classification only — never the raw provider exception (Phase 7 scope).</summary>
        public const string SendFailed = "ResponsibilityTerm.SendFailed";

        /// <summary>Recorded with safe metadata only (termId, documentNumber, version, signedArtifactId) — never StorageKey/PDF bytes (Phase 7 scope).</summary>
        public const string SignedDocumentUploaded = "ResponsibilityTerm.SignedDocumentUploaded";

        /// <summary>Mirrors <see cref="Downloaded"/>: the signed artifact also contains Consultant PII, so who accessed it is deliberately recorded.</summary>
        public const string SignedDocumentDownloaded = "ResponsibilityTerm.SignedDocumentDownloaded";
    }

    public static class System
    {
        public const string AdministrationUserBootstrapped = "System.AdministrationUserBootstrapped";
    }

    /// <summary>
    /// Only the terminal outcome is audited — no <c>Previewed</c> action
    /// exists (Phase 8 scope): a preview persists nothing and
    /// commits no PII to any durable record beyond the ImportSession itself,
    /// so an additional audit event would be noise without operational
    /// value. <see cref="Completed"/> carries counts and <c>fileSha256</c>
    /// only — never row values (NIF/email/address/phone).
    /// </summary>
    public static class ConsultantImport
    {
        public const string Completed = "ConsultantImport.Completed";

        /// <summary>Recorded when a Pending session is explicitly cancelled (Phase 8 retention review) — safe metadata only, same discipline as <see cref="Completed"/>.</summary>
        public const string Cancelled = "ConsultantImport.Cancelled";
    }

    /// <summary>See <see cref="ConsultantImport"/>'s remarks — same discipline, never SerialNumbers/Notes.</summary>
    public static class EquipmentImport
    {
        public const string Completed = "EquipmentImport.Completed";

        public const string Cancelled = "EquipmentImport.Cancelled";
    }

    /// <summary>
    /// See <see cref="ConsultantImport"/>'s remarks — same discipline: counts,
    /// created entity ids, the file hash and the import session id only;
    /// never names, NIFs, contact details, serial numbers, IMEIs or workbook
    /// free text (and PIN/PUK never exist anywhere in this workflow).
    /// </summary>
    public static class CurrentInventoryImport
    {
        public const string Completed = "CurrentInventoryImport.Completed";

        public const string Cancelled = "CurrentInventoryImport.Cancelled";
    }

    public static class ConsultantExport
    {
        /// <summary>Row count and filter summary only — never the exported PII itself (Phase 8 scope).</summary>
        public const string Generated = "ConsultantExport.Generated";
    }

    public static class EquipmentExport
    {
        public const string Generated = "EquipmentExport.Generated";
    }
}
