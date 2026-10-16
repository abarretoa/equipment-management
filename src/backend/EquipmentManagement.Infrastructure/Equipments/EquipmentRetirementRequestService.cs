using System.Data;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Email;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EquipmentManagement.Infrastructure.Equipments;

/// <summary>
/// HR requests that Administration retire eligible equipment; Administration
/// reviews and decides. This is the only place that creates/decides an
/// <see cref="EquipmentRetirementRequest"/>. Approval never re-implements
/// retirement — it delegates to <see cref="EquipmentService.RetireAsync"/>,
/// the single existing Administration-only retirement operation, inside the
/// same Serializable transaction, so "the request became Approved" and "the
/// equipment actually retired" can never disagree (see <c>ApproveAsync</c>'s
/// remarks). Deliberately scoped to exactly this workflow — not a generic
/// approval engine.
/// </summary>
public sealed partial class EquipmentRetirementRequestService(
    ApplicationDbContext dbContext,
    EquipmentService equipmentService,
    AuditService auditService,
    IEmailSender emailSender,
    EmailFailureLogger emailFailureLogger,
    UserManager<ApplicationUser> userManager,
    IOptions<ApplicationLinkOptions> linkOptions,
    TimeProvider timeProvider,
    ILogger<EquipmentRetirementRequestService> logger)
{
    public async Task<PagedResult<EquipmentRetirementRequestRecord>> ListAsync(
        EquipmentRetirementRequestListFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query =
            from r in dbContext.EquipmentRetirementRequests.AsNoTracking()
            join e in dbContext.Equipment.AsNoTracking() on r.EquipmentId equals e.Id
            join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
            select new { Request = r, AssetTag = e.AssetTag, TypeName = t.Name, e.Manufacturer, e.Model, EquipmentStatus = e.Status };

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Request.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(x => x.Request.RequestedAtUtc)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .Select(x => new EquipmentRetirementRequestRecord(
                x.Request.Id, x.Request.EquipmentId, x.AssetTag, x.TypeName, x.Manufacturer, x.Model, x.EquipmentStatus,
                x.Request.RequestedByUserId, x.Request.RequestedByDisplayNameSnapshot, x.Request.RequestedAtUtc, x.Request.RequestReason,
                x.Request.Status, x.Request.DecidedByUserId, x.Request.DecidedByDisplayNameSnapshot, x.Request.DecidedAtUtc,
                x.Request.DecisionReason))
            .ToListAsync(cancellationToken);

        return new PagedResult<EquipmentRetirementRequestRecord>(items, page1Based, boundedPageSize, totalCount);
    }

    /// <summary>
    /// Persists the request and commits before ever attempting the
    /// Administration email notification (docs: "request creation must NOT
    /// depend on successful email delivery") — a delivery failure is caught,
    /// logged, and never surfaces to the caller or touches the already-
    /// committed Pending request.
    /// </summary>
    public async Task<ServiceResult<EquipmentRetirementRequestRecord>> CreateAsync(
        Guid equipmentId, string reason, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ServiceResult.Fail<EquipmentRetirementRequestRecord>(ServiceErrorCode.Validation, "A reason is required.");
        }

        // Serializable, same rationale as MaintenanceService.StartAsync: the
        // "is this equipment still eligible / is there already a Pending
        // request" read must sit inside the same snapshot as the insert. The
        // partial unique index on EquipmentRetirementRequests(EquipmentId)
        // WHERE Status = 'Pending' is the database-enforced backstop for the
        // fresh-insert race specifically.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var equipment = await dbContext.Equipment.FirstOrDefaultAsync(e => e.Id == equipmentId, cancellationToken);
        if (equipment is null)
        {
            return ServiceResult.Fail<EquipmentRetirementRequestRecord>(ServiceErrorCode.NotFound, "Equipment not found.");
        }

        if (!EquipmentRetirementRequestRules.CanRequest(equipment.Status))
        {
            return ServiceResult.Fail<EquipmentRetirementRequestRecord>(
                ServiceErrorCode.InvalidLifecycleTransition,
                $"Equipment in status '{equipment.Status}' is not eligible for a retirement request.");
        }

        var alreadyPending = await dbContext.EquipmentRetirementRequests.AnyAsync(
            r => r.EquipmentId == equipmentId && r.Status == EquipmentRetirementRequestStatus.Pending, cancellationToken);
        if (alreadyPending)
        {
            return ServiceResult.Fail<EquipmentRetirementRequestRecord>(
                ServiceErrorCode.RetirementRequestAlreadyPending, "This equipment already has a pending retirement request.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var request = new EquipmentRetirementRequest
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            RequestedByUserId = actorUserId,
            RequestedByDisplayNameSnapshot = actorDisplayName,
            RequestedAtUtc = now,
            RequestReason = reason.Trim(),
            Status = EquipmentRetirementRequestStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        dbContext.EquipmentRetirementRequests.Add(request);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            // Deliberately no RequestReason in the audit payload — same
            // discipline as AuditActions.EquipmentReturn.Cancelled's free-text
            // reason (never copied into the audit trail).
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentRetirementRequest.Created,
                EntityType: "Equipment",
                EntityId: equipment.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { equipment.AssetTag, RetirementRequestId = request.Id }), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.Find(ex, PostgresErrorCodes.UniqueViolation, PostgresErrorCodes.SerializationFailure) is not null)
        {
            return ServiceResult.Fail<EquipmentRetirementRequestRecord>(
                ServiceErrorCode.RetirementRequestAlreadyPending, "This equipment already has a pending retirement request.");
        }

        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);
        var spec = await dbContext.LaptopSpecifications.AsNoTracking().FirstOrDefaultAsync(s => s.EquipmentId == equipmentId, cancellationToken);
        var laptopSpecification = spec is null ? null : new LaptopSpecificationRecord(spec.Processor, spec.Memory, spec.Storage);

        await NotifyAdministrationAsync(equipment.AssetTag, type.Name, equipment.Manufacturer, equipment.Model, laptopSpecification, request, cancellationToken);

        return ServiceResult.Ok(ToRecord(request, equipment.AssetTag, type.Name, equipment.Manufacturer, equipment.Model, equipment.Status));
    }

    /// <summary>
    /// Re-validates the request AND the equipment's current lifecycle, then
    /// executes retirement through the existing, single
    /// <see cref="EquipmentService.RetireAsync"/> operation — never a second
    /// implementation. That call, sharing this method's <see cref="ApplicationDbContext"/>
    /// and Serializable transaction, is also where the request row itself is
    /// flipped to Approved (see its remarks on resolving orphaned Pending
    /// requests) — so "the request is Approved" and "the equipment is
    /// Retired" are written together, or neither is. If retirement fails
    /// (e.g. the equipment's lifecycle changed since the request was made),
    /// nothing is committed and the request remains Pending.
    /// </summary>
    public async Task<ServiceResult<EquipmentRetirementRequestRecord>> ApproveAsync(
        Guid requestId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var request = await dbContext.EquipmentRetirementRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return ServiceResult.Fail<EquipmentRetirementRequestRecord>(ServiceErrorCode.NotFound, "Retirement request not found.");
        }

        if (!EquipmentRetirementRequestRules.CanDecide(request.Status))
        {
            return ServiceResult.Fail<EquipmentRetirementRequestRecord>(
                ServiceErrorCode.RetirementRequestNotPending, "This retirement request has already been decided.");
        }

        ServiceResult<EquipmentRecord> retireResult;
        try
        {
            retireResult = await equipmentService.RetireAsync(request.EquipmentId, actorUserId, actorDisplayName, cancellationToken);
            if (!retireResult.Succeeded)
            {
                // Nothing was written (RetireAsync fails before any SaveChanges),
                // so the transaction is simply abandoned — the request stays
                // exactly as it was, Pending, for Administration to review/reject.
                return ServiceResult.Fail<EquipmentRetirementRequestRecord>(retireResult.ErrorCode, retireResult.ErrorMessage!);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsSerializationFailure(ex))
        {
            // This method owns the Serializable transaction RetireAsync joined,
            // so a conflicting lifecycle change (e.g. a dispatch of the same
            // item) committing first surfaces here — at RetireAsync's write or
            // at this commit. Nothing was committed: the request stays Pending
            // and the equipment keeps the other operation's status.
            dbContext.ChangeTracker.Clear();
            return ServiceResult.Fail<EquipmentRetirementRequestRecord>(
                ServiceErrorCode.Concurrency, "Este equipamento foi alterado por outra operação. Atualize a página e tente novamente.");
        }

        // RetireAsync's own write already set this request's Status/Decided*
        // fields (loaded on the same tracked DbContext) — reload the entity's
        // current values rather than re-deriving them here.
        await dbContext.Entry(request).ReloadAsync(cancellationToken);

        var equipment = retireResult.Value!;
        return ServiceResult.Ok(ToRecord(request, equipment.AssetTag, equipment.EquipmentTypeName, equipment.Manufacturer, equipment.Model, equipment.Status));
    }

    public async Task<ServiceResult<EquipmentRetirementRequestRecord>> RejectAsync(
        Guid requestId, string decisionReason, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(decisionReason))
        {
            return ServiceResult.Fail<EquipmentRetirementRequestRecord>(ServiceErrorCode.Validation, "A rejection reason is required.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var request = await dbContext.EquipmentRetirementRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return ServiceResult.Fail<EquipmentRetirementRequestRecord>(ServiceErrorCode.NotFound, "Retirement request not found.");
        }

        if (!EquipmentRetirementRequestRules.CanDecide(request.Status))
        {
            return ServiceResult.Fail<EquipmentRetirementRequestRecord>(
                ServiceErrorCode.RetirementRequestNotPending, "This retirement request has already been decided.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        request.Status = EquipmentRetirementRequestStatus.Rejected;
        request.DecidedByUserId = actorUserId;
        request.DecidedByDisplayNameSnapshot = actorDisplayName;
        request.DecidedAtUtc = now;
        request.DecisionReason = decisionReason.Trim();
        request.UpdatedAtUtc = now;

        var equipment = await dbContext.Equipment.AsNoTracking().FirstAsync(e => e.Id == request.EquipmentId, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        // Deliberately no DecisionReason in the audit payload — same reason-
        // stays-out-of-audit discipline as request creation above.
        await auditService.RecordAsync(new AuditEntry(
            AuditActions.EquipmentRetirementRequest.Rejected,
            EntityType: "Equipment",
            EntityId: equipment.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { equipment.AssetTag, RetirementRequestId = request.Id }), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);
        return ServiceResult.Ok(ToRecord(request, equipment.AssetTag, type.Name, equipment.Manufacturer, equipment.Model, equipment.Status));
    }

    /// <summary>
    /// Best-effort fan-out: one individual message per active Administration
    /// user (never a shared CC, so recipients never see each other's address).
    /// Every failure — resolving recipients, or one specific send — is caught
    /// and logged, never rethrown; the caller's already-committed Pending
    /// request is never affected (docs: email delivery semantics).
    /// </summary>
    private async Task NotifyAdministrationAsync(
        string assetTag,
        string equipmentTypeName,
        string? manufacturer,
        string? model,
        LaptopSpecificationRecord? laptopSpecification,
        EquipmentRetirementRequest request,
        CancellationToken cancellationToken)
    {
        IList<ApplicationUser> administrators;
        try
        {
            administrators = await userManager.GetUsersInRoleAsync(Roles.Administration);
        }
        catch (Exception ex)
        {
            LogRecipientResolutionFailed(logger, ex);
            return;
        }

        var activeAdministrators = administrators.Where(u => u.IsActive && !string.IsNullOrWhiteSpace(u.Email)).ToList();
        if (activeAdministrators.Count == 0)
        {
            return;
        }

        var reviewUrl = linkOptions.Value.BuildUrl("/solicitacoes-abate");
        var (subject, body) = EquipmentRetirementRequestEmailTemplate.Build(
            assetTag, equipmentTypeName, manufacturer, model, laptopSpecification,
            request.RequestReason, request.RequestedByDisplayNameSnapshot, request.RequestedAtUtc, reviewUrl);

        foreach (var administrator in activeAdministrators)
        {
            try
            {
                await emailSender.SendAsync(new EmailMessage(administrator.Email!, subject, body), cancellationToken);
            }
            catch (EmailSendException ex)
            {
                // This message has no attempt row of its own; the request that
                // triggered it is the identifier an operator can look up.
                emailFailureLogger.DeliveryFailed(EmailMessageTypes.RetirementRequestNotification, request.Id, ex);
            }
        }
    }

    private static EquipmentRetirementRequestRecord ToRecord(
        EquipmentRetirementRequest r, string assetTag, string equipmentTypeName, string? manufacturer, string? model, EquipmentStatus equipmentStatus) => new(
        r.Id, r.EquipmentId, assetTag, equipmentTypeName, manufacturer, model, equipmentStatus,
        r.RequestedByUserId, r.RequestedByDisplayNameSnapshot, r.RequestedAtUtc, r.RequestReason,
        r.Status, r.DecidedByUserId, r.DecidedByDisplayNameSnapshot, r.DecidedAtUtc, r.DecisionReason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not resolve active Administration recipients for a retirement request notification.")]
    private static partial void LogRecipientResolutionFailed(ILogger logger, Exception ex);}
