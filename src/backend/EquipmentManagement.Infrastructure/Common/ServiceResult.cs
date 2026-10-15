namespace EquipmentManagement.Infrastructure.Common;

/// <summary>
/// The set of expected, named business-rule failures a service method can return.
/// Deliberately not exceptions: these are anticipated outcomes a caller (a
/// controller) must branch on to pick an HTTP status, not exceptional program
/// states — see docs/PROJECT_SPEC.md, section 27 (predictable error responses).
/// </summary>
public enum ServiceErrorCode
{
    None,
    NotFound,
    DuplicateEmail,
    UnsupportedRole,
    AddressRequiredForRole,
    LastAdministrator,
    Concurrency,
    Validation,

    // --- Phase 2 (Companies/Consultants/Allocations) ---
    DuplicateName,
    DuplicateNif,
    InactiveReference,

    /// <summary>The Consultant already has their one Active allocation; it must be ended before a new one is registered (dates never matter).</summary>
    ActiveAllocationAlreadyExists,
    AllocationNotActive,

    // --- Phase 3 (EquipmentTypes/Equipment) ---
    DuplicateSerialNumber,
    InvalidLifecycleTransition,

    /// <summary>Another mobile phone already uses this IMEI — IMEI is unique product-wide.</summary>
    DuplicateImei,

    // --- Phase 4 (Handovers/Assignments/Inspections/Photos) ---
    HandoverNotDraft,
    EquipmentAlreadyInHandover,
    InspectionMissing,
    InsufficientPhotos,
    InvalidFile,
    FileTooLarge,

    // --- Phase 5 (Returns/Damage/Maintenance) ---
    ReturnAlreadyInProgress,
    ReturnNotPending,
    ReturnNotReceived,
    DamageEvidenceRequired,
    MaintenanceNotEligible,
    MaintenanceNotActive,

    // --- Phase 6 (Responsibility Terms) ---
    HandoverNotFinalized,
    SnapshotIncomplete,

    // --- Phase 7 (Responsibility Term delivery/signature) ---
    /// <summary>A send/resend was attempted against a Superseded term — only the current (Generated) version may start a new send (Phase 7 scope).</summary>
    TermNotCurrent,

    /// <summary>No recipient email could be determined (no historical Consultant snapshot email and no override supplied) or an override failed syntax validation.</summary>
    InvalidRecipient,

    /// <summary>The configured mail provider rejected the send attempt or is unreachable — the delivery attempt was still recorded (Phase 7 scope).</summary>
    EmailSendFailed,

    /// <summary>A signed artifact already exists for this exact ResponsibilityTerm version (Phase 7 scope) — no overwrite path in this phase.</summary>
    SignedArtifactAlreadyExists,

    // --- Phase 8 (Consultant/Equipment Excel import-export) ---
    /// <summary>The uploaded file is not a genuine, readable .xlsx workbook (wrong extension/content-type, corrupt ZIP/OOXML structure, or a macro-enabled .xlsm).</summary>
    UnsupportedWorkbook,

    /// <summary>The uploaded file exceeds <c>ImportLimitsOptions.MaxUploadBytes</c>.</summary>
    WorkbookTooLarge,

    /// <summary>The workbook opened but violates a structural limit (too many worksheets/rows/columns) or is otherwise malformed beyond the basic open check.</summary>
    InvalidWorkbookStructure,

    /// <summary>The workbook has no readable worksheet.</summary>
    MissingWorksheet,

    /// <summary>A required canonical column header (e.g. NIF, FullName, EquipmentType) is missing, or a canonical header is duplicated.</summary>
    MissingRequiredColumn,

    /// <summary>The ImportSession has at least one Error-severity row and cannot be confirmed until corrected and re-previewed (Phase 8 scope).</summary>
    ImportPreviewHasErrors,

    /// <summary>The ImportSession is not in a confirmable state — already Completed or Cancelled. Distinct from <see cref="ImportSessionExpired"/>, which is specifically the time-based case.</summary>
    ImportSessionNotConfirmable,

    /// <summary>
    /// The ImportSession is still Pending but past its validity window
    /// (Phase 8 retention review) — HR must re-upload and re-preview; the
    /// row snapshot has been, or is being, discarded and is never revived.
    /// </summary>
    ImportSessionExpired,

    /// <summary>Database state changed between preview and confirmation for at least one row — nothing was persisted; see the updated issues (Phase 8 scope).</summary>
    ImportPreviewStale,

    // --- Equipment Retirement Requests ---
    /// <summary>This equipment already has a Pending retirement request — enforced at both application and database level (partial unique index).</summary>
    RetirementRequestAlreadyPending,

    /// <summary>The request is not Pending — it was already Approved or Rejected and cannot be decided again.</summary>
    RetirementRequestNotPending,

    // --- Equipment condition photos / dispatch / collection notifications ---

    /// <summary>A Laptop/MobilePhone equipment has no current <c>EquipmentPhotoSet</c> — required before Handover finalization or a photo-bearing Responsibility Term can be generated.</summary>
    MissingCurrentPhotoSet,

    /// <summary>The legacy photo-backfill action was attempted on equipment that already has a current photo set — use the Return workflow to refresh it instead.</summary>
    PhotoSetAlreadyExists,

    /// <summary>This Handover already has a registered dispatch — its shipment composition (items) can no longer be changed.</summary>
    HandoverCompositionLocked,

    /// <summary>This Handover already has a registered dispatch — dispatch cannot be registered twice.</summary>
    DispatchAlreadyRegistered,

    /// <summary>Ending an allocation as end-of-engagement for a consultant with active equipment requires an expected collection date.</summary>
    CollectionEstimateRequired,

    // --- Iteration 3 (handover delivery workflow) ---

    /// <summary>The Consultant has no Active allocation, so cannot receive a new handover, and a Draft handover for them cannot be dispatched or confirmed.</summary>
    ConsultantNotEligible,

    /// <summary>A legacy Draft handover has no delivery method recorded; one must be chosen before dispatch or confirmation.</summary>
    DeliveryMethodRequired,

    /// <summary>"Registar envio" was attempted on a handover whose delivery method is not Carrier.</summary>
    DispatchNotApplicable,

    /// <summary>A Carrier handover was confirmed before its dispatch was registered.</summary>
    DispatchRequired,

    /// <summary>The Consultant has more than one Active allocation — invalid data the one-Active invariant should make impossible. Never resolved by guessing which one applies.</summary>
    InconsistentAllocationState,

    // --- Iteration 4 (end-of-engagement return initiation) ---

    /// <summary>An engagement cannot be ended because equipment in the Consultant's active custody is in a state that allows neither initiating its return nor treating one as already pending. Nothing is changed; the equipment must be corrected first.</summary>
    EquipmentRequiresAttention,

    /// <summary>Ending the engagement would initiate equipment returns, and the caller does not hold <c>Equipment.Return</c>.</summary>
    ReturnPermissionRequired,
}

/// <summary>
/// The outcome of a service operation with no return value.
/// </summary>
public sealed class ServiceResult
{
    private ServiceResult(bool succeeded, ServiceErrorCode errorCode, string? errorMessage)
    {
        Succeeded = succeeded;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }

    public ServiceErrorCode ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static ServiceResult Ok() => new(true, ServiceErrorCode.None, null);

    public static ServiceResult Fail(ServiceErrorCode code, string message) => new(false, code, message);

    // CA1000 forbids static factory methods on ServiceResult<T> itself (a
    // generic type), so the factories for it live here instead, on the
    // non-generic sibling — ServiceResult.Ok(value) / ServiceResult.Fail<T>(...).
    public static ServiceResult<T> Ok<T>(T value) => new(true, value, ServiceErrorCode.None, null);

    public static ServiceResult<T> Fail<T>(ServiceErrorCode code, string message) => new(false, default, code, message);
}

/// <summary>The outcome of a service operation that returns <typeparamref name="T"/> on success.</summary>
public sealed class ServiceResult<T>
{
    internal ServiceResult(bool succeeded, T? value, ServiceErrorCode errorCode, string? errorMessage)
    {
        Succeeded = succeeded;
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }

    public T? Value { get; }

    public ServiceErrorCode ErrorCode { get; }

    public string? ErrorMessage { get; }
}
