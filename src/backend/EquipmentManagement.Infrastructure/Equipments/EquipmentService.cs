using System.Security.Cryptography;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Files;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Files;
using EquipmentManagement.Infrastructure.Handovers;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EquipmentManagement.Infrastructure.Equipments;

/// <summary>
/// Physical-asset registry management (docs/PROJECT_SPEC.md, section 7). No
/// hard-delete method exists here by design — equipment is retired, never
/// deleted (docs/PROJECT_SPEC.md, section 16). Carries no consultant/assignment
/// reference and no code path here can set any lifecycle status besides
/// <see cref="EquipmentStatus.Available"/> (creation) and
/// <see cref="EquipmentStatus.Retired"/> (<see cref="RetireAsync"/>) — see
/// <c>Domain.Equipments.EquipmentLifecycleRules</c>.
///
/// Condition photos are written by exactly two events here — <see cref="CreateAsync"/>
/// (the equipment's first current photo set) and <see cref="EstablishPhotosAsync"/>
/// (the legacy-backfill action, for photo-required equipment that predates this
/// feature and has no current set yet) — never <see cref="UpdateAsync"/>, which
/// has no photo parameter at all and can never create/replace a photo set.
/// Return physical receipt (<c>EquipmentReturnService.ReceiveAsync</c>) is the
/// only other writer, for the ongoing condition-refresh event.
/// </summary>
public sealed class EquipmentService(
    ApplicationDbContext dbContext,
    AssetTagGenerator assetTagGenerator,
    AuditService auditService,
    IPrivateFileStorage fileStorage,
    IOptions<PrivateStorageOptions> storageOptions,
    TimeProvider timeProvider)
{
    // Equipment has two independent unique constraints (see the Phase 3
    // migration): a collision here is expected to be the caller-supplied
    // serial number. A collision on IX_Equipment_AssetTag instead would mean
    // AssetTagGenerator produced a duplicate — an infrastructure bug, not a
    // user-facing conflict — so it deliberately does NOT match here and
    // propagates as an unexpected failure instead of being mislabeled.
    private const string SerialNumberConstraintName = "IX_Equipment_NormalizedSerialNumber";

    // IMEI is unique product-wide: a collision here is a caller-supplied IMEI
    // already used by another mobile phone (a concurrent create/update that
    // slipped past the explicit pre-check).
    private const string ImeiConstraintName = "IX_MobilePhoneSpecifications_NormalizedImei";

    /// <summary>pt-PT: reaches the person registering or editing the phone as the response title.</summary>
    public const string DuplicateImeiMessage = "Já existe um telemóvel registado com este IMEI.";

    /// <summary>pt-PT: no IMEI at all (blank, or separators only — <see cref="MobilePhoneSpecificationRules.IsComplete"/>).</summary>
    public const string RequiredImeiMessage = "O IMEI é obrigatório para telemóveis.";

    /// <summary>pt-PT: a present but malformed IMEI (<see cref="MobilePhoneSpecificationRules.IsValidImei"/>).</summary>
    public const string InvalidImeiMessage = "O IMEI deve conter exatamente 15 dígitos.";

    public async Task<PagedResult<EquipmentSummaryRecord>> ListAsync(
        EquipmentListFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // Current consultant = the (at most one, by the partial-unique-index
        // invariant) Active-status EquipmentAssignment for this equipment. A
        // left join keeps unassigned equipment in the result set.
        var query =
            from e in dbContext.Equipment.AsNoTracking()
            join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
            join a in dbContext.EquipmentAssignments.AsNoTracking().Where(a => a.Status == AssignmentStatus.Active)
                on e.Id equals a.EquipmentId into assignments
            from currentAssignment in assignments.DefaultIfEmpty()
            select new { Equipment = e, TypeName = t.Name, CurrentAssignment = currentAssignment };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Equipment.AssetTag, $"%{filter.Search}%") ||
                (x.Equipment.SerialNumber != null && EF.Functions.ILike(x.Equipment.SerialNumber, $"%{filter.Search}%")) ||
                (x.Equipment.Manufacturer != null && EF.Functions.ILike(x.Equipment.Manufacturer, $"%{filter.Search}%")) ||
                (x.Equipment.Model != null && EF.Functions.ILike(x.Equipment.Model, $"%{filter.Search}%")));
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Equipment.Status == status);
        }

        if (filter.EquipmentTypeId is { } equipmentTypeId)
        {
            query = query.Where(x => x.Equipment.EquipmentTypeId == equipmentTypeId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Manufacturer))
        {
            query = query.Where(x => x.Equipment.Manufacturer != null && EF.Functions.ILike(x.Equipment.Manufacturer, $"%{filter.Manufacturer}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page1Based = Math.Max(page, 1);
        var boundedPageSize = Math.Clamp(pageSize, 1, 100);

        var page1 = await query
            .OrderByDescending(x => x.Equipment.CreatedAtUtc)
            .Skip((page1Based - 1) * boundedPageSize)
            .Take(boundedPageSize)
            .ToListAsync(cancellationToken);

        // Consultant name is fetched separately (a small, bounded set of ids: at
        // most one per page row) rather than joined above, to keep the main
        // query a simple left join instead of a three-way join.
        var consultantIds = page1.Where(x => x.CurrentAssignment is not null)
            .Select(x => x.CurrentAssignment!.ConsultantId).Distinct().ToList();
        var consultantNames = consultantIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Consultants.AsNoTracking()
                .Where(c => consultantIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FullName, cancellationToken);

        var items = page1.Select(x => new EquipmentSummaryRecord(
            x.Equipment.Id, x.Equipment.AssetTag, x.TypeName, x.Equipment.Manufacturer, x.Equipment.Model,
            x.Equipment.SerialNumber, x.Equipment.Status, x.Equipment.CreatedAtUtc,
            x.CurrentAssignment?.ConsultantId,
            x.CurrentAssignment?.ConsultantId is { } id ? consultantNames.GetValueOrDefault(id) : null)).ToList();

        return new PagedResult<EquipmentSummaryRecord>(items, page1Based, boundedPageSize, totalCount);
    }

    /// <summary>
    /// <paramref name="equipmentTypeId"/> scopes the entire aggregate (both
    /// <c>ByStatus</c> and <c>AvailableByType</c>) to that one type — used by
    /// the dashboard's Equipment-Type click filter (Iteration 1). Always counts the complete matching dataset with SQL
    /// GROUP BY, never a paginated in-memory subset. A type id that matches no
    /// <see cref="EquipmentType"/> row simply yields all-zero counts, the same
    /// as any other aggregate query over an empty match set — not a 404,
    /// since this endpoint has no single-resource identity to 404 on.
    /// </summary>
    public async Task<EquipmentSummaryStats> GetSummaryAsync(Guid? equipmentTypeId = null, CancellationToken cancellationToken = default)
    {
        var equipmentQuery = dbContext.Equipment.AsNoTracking().AsQueryable();
        if (equipmentTypeId is { } typeId)
        {
            equipmentQuery = equipmentQuery.Where(e => e.EquipmentTypeId == typeId);
        }

        var byStatus = await equipmentQuery
            .GroupBy(e => e.Status)
            .Select(g => new EquipmentStatusCountRecord(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        // OrderByDescending is applied after materialization, not chained onto the
        // query — EF Core cannot translate an ORDER BY over a projected record's
        // property when it sits after a GroupBy/Select into a non-entity type.
        var availableByType = (await (
                from e in equipmentQuery
                join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
                where e.Status == EquipmentStatus.Available
                group e by new { t.Id, t.Name } into g
                select new EquipmentTypeCountRecord(g.Key.Id, g.Key.Name, g.Count()))
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.Count)
            .ToList();

        return new EquipmentSummaryStats(byStatus.Sum(s => s.Count), byStatus, availableByType);
    }

    public async Task<EquipmentRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await (
                from e in dbContext.Equipment.AsNoTracking()
                join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
                where e.Id == id
                select new { Equipment = e, TypeName = t.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        var spec = await dbContext.LaptopSpecifications.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EquipmentId == id, cancellationToken);
        var mobileSpec = await dbContext.MobilePhoneSpecifications.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EquipmentId == id, cancellationToken);
        var custody = await GetCurrentCustodyAsync(id, cancellationToken);
        var pendingRequest = await GetPendingRetirementRequestAsync(id, cancellationToken);
        var photoSet = await GetCurrentPhotoSetAsync(id, cancellationToken);
        // Presentation hint only ("Alterar fotografias" visibility) — the
        // replacement endpoint re-checks everything itself.
        var photosCorrectable = photoSet is not null &&
            await ResolvePhotoCorrectionAsync(id, result.Equipment.Status, cancellationToken) == CurrentPhotoCorrectionEligibility.Eligible;

        return ToRecord(result.Equipment, result.TypeName, spec, custody, pendingRequest, mobileSpec, photoSet, photosCorrectable);
    }

    /// <summary>
    /// <paramref name="skipConditionPhotoRequirement"/> exists only for
    /// <c>EquipmentImportService</c>'s bulk Excel import, which has no way to
    /// attach photo files to a spreadsheet row — an imported Laptop/MobilePhone
    /// row is created in the same "legacy, no current photo set" state as
    /// equipment that predates this feature entirely, and HR establishes its
    /// first photo set afterward via <see cref="EstablishPhotosAsync"/>. Every
    /// other caller (the normal HTTP creation flow) must leave this false.
    /// </summary>
    public async Task<ServiceResult<EquipmentRecord>> CreateAsync(
        CreateEquipmentData data, IReadOnlyList<EquipmentPhotoFile> photos, Guid actorUserId, string actorDisplayName,
        bool skipConditionPhotoRequirement = false, CancellationToken cancellationToken = default)
    {
        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == data.EquipmentTypeId, cancellationToken);
        if (type is null)
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.Validation, "Equipment type not found.");
        }

        if (!type.IsActive)
        {
            return ServiceResult.Fail<EquipmentRecord>(
                ServiceErrorCode.InactiveReference, "An inactive equipment type cannot be used to register new equipment.");
        }

        var validation = ValidateAgainstKind(type.Kind, data.Manufacturer, data.SerialNumber, data.LaptopSpecification, data.MobilePhoneSpecification);
        if (validation is not null)
        {
            return ServiceResult.Fail<EquipmentRecord>(validation.Value.Code, validation.Value.Message);
        }

        var photosRequired = EquipmentRules.RequiresConditionPhotos(type.Kind) && !skipConditionPhotoRequirement;
        if (photosRequired && photos.Count != RequiredPhotoCount)
        {
            return ServiceResult.Fail<EquipmentRecord>(
                ServiceErrorCode.Validation, $"É necessário enviar exatamente {RequiredPhotoCount} fotografias do estado do equipamento.");
        }

        if (!photosRequired && photos.Count > 0)
        {
            return ServiceResult.Fail<EquipmentRecord>(
                ServiceErrorCode.Validation, "Este tipo de equipamento não requer fotografias.");
        }

        var preparedPhotos = new List<PreparedPhoto>();
        foreach (var photo in photos)
        {
            var (error, prepared) = await ValidateAndReadImageAsync(photo, cancellationToken);
            if (error is { } validationError)
            {
                await CleanupOrphanedPhotosAsync(preparedPhotos, cancellationToken);
                return ServiceResult.Fail<EquipmentRecord>(validationError.Code, validationError.Message);
            }

            preparedPhotos.Add(prepared!);
        }

        var normalizedSerial = EquipmentRules.NormalizeSerialNumber(data.SerialNumber);
        if (normalizedSerial is not null &&
            await dbContext.Equipment.AnyAsync(e => e.NormalizedSerialNumber == normalizedSerial, cancellationToken))
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.DuplicateSerialNumber, "Equipment with this serial number already exists.");
        }

        var normalizedImei = type.Kind == EquipmentTypeKind.MobilePhone
            ? MobilePhoneSpecificationRules.NormalizeImei(data.MobilePhoneSpecification!.Imei)
            : null;
        if (normalizedImei is not null &&
            await dbContext.MobilePhoneSpecifications.AnyAsync(s => s.NormalizedImei == normalizedImei, cancellationToken))
        {
            await CleanupOrphanedPhotosAsync(preparedPhotos, cancellationToken);
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.DuplicateImei, DuplicateImeiMessage);
        }

        var assetTag = await assetTagGenerator.GenerateAsync(type.AssetTagPrefix, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var equipment = new Domain.Equipments.Equipment
        {
            Id = Guid.NewGuid(),
            EquipmentTypeId = type.Id,
            AssetTag = assetTag,
            Manufacturer = Trim(data.Manufacturer),
            Model = Trim(data.Model),
            SerialNumber = Trim(data.SerialNumber),
            NormalizedSerialNumber = normalizedSerial,
            Notes = Trim(data.Notes),
            Status = EquipmentStatus.Available,
            PurchaseDate = data.PurchaseDate,
            InvoiceNumber = Trim(data.InvoiceNumber),
            PurchaseVendor = Trim(data.PurchaseVendor),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        dbContext.Equipment.Add(equipment);

        LaptopSpecification? spec = null;
        if (type.Kind == EquipmentTypeKind.Laptop)
        {
            spec = new LaptopSpecification
            {
                EquipmentId = equipment.Id,
                Processor = data.LaptopSpecification!.Processor.Trim(),
                Memory = data.LaptopSpecification.Memory.Trim(),
                Storage = data.LaptopSpecification.Storage.Trim(),
            };
            dbContext.LaptopSpecifications.Add(spec);
        }

        MobilePhoneSpecification? mobileSpec = null;
        if (type.Kind == EquipmentTypeKind.MobilePhone)
        {
            mobileSpec = new MobilePhoneSpecification
            {
                EquipmentId = equipment.Id,
                Imei = data.MobilePhoneSpecification!.Imei.Trim(),
                NormalizedImei = normalizedImei!,
                MobileNumber = Trim(data.MobilePhoneSpecification.MobileNumber),
                SimSerialNumber = Trim(data.MobilePhoneSpecification.SimSerialNumber),
            };
            dbContext.MobilePhoneSpecifications.Add(mobileSpec);
        }

        EquipmentPhotoSet? photoSet = null;
        if (photosRequired)
        {
            photoSet = new EquipmentPhotoSet
            {
                Id = Guid.NewGuid(),
                EquipmentId = equipment.Id,
                Status = PhotoSetStatus.Current,
                Source = PhotoSetSource.InitialRegistration,
                CreatedAtUtc = now,
                CreatedByUserId = actorUserId,
                CreatedByDisplayNameSnapshot = actorDisplayName,
            };
            dbContext.EquipmentPhotoSets.Add(photoSet);

            var sortOrder = 1;
            foreach (var photo in preparedPhotos)
            {
                dbContext.EquipmentPhotoSetPhotos.Add(new EquipmentPhotoSetPhoto
                {
                    Id = Guid.NewGuid(),
                    EquipmentPhotoSetId = photoSet.Id,
                    StorageKey = photo.StorageKey,
                    OriginalFileName = photo.OriginalFileName,
                    ContentType = photo.ContentType,
                    SizeBytes = photo.SizeBytes,
                    Sha256Hash = photo.Sha256,
                    SortOrder = sortOrder++,
                    UploadedAtUtc = now,
                    UploadedByUserId = actorUserId,
                });
            }
        }

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolationOn(ex, SerialNumberConstraintName))
        {
            await CleanupOrphanedPhotosAsync(preparedPhotos, cancellationToken);
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.DuplicateSerialNumber, "Equipment with this serial number already exists.");
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolationOn(ex, ImeiConstraintName))
        {
            await CleanupOrphanedPhotosAsync(preparedPhotos, cancellationToken);
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.DuplicateImei, DuplicateImeiMessage);
        }

        try
        {
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.Equipment.Created,
                EntityType: "Equipment",
                EntityId: equipment.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new
                {
                    equipment.AssetTag,
                    EquipmentTypeName = type.Name,
                    equipment.Manufacturer,
                    equipment.Model,
                    equipment.SerialNumber,
                }), cancellationToken);

            if (photoSet is not null)
            {
                await auditService.RecordAsync(new AuditEntry(
                    AuditActions.EquipmentPhotoSet.Established,
                    EntityType: "Equipment",
                    EntityId: equipment.Id.ToString(),
                    ActorUserId: actorUserId,
                    ActorDisplayNameSnapshot: actorDisplayName,
                    NewValues: new { equipment.AssetTag, PhotoSetId = photoSet.Id, Source = photoSet.Source.ToString() }), cancellationToken);
            }

            await transaction.CommitIfOwnedAsync(cancellationToken);
        }
        catch
        {
            // Nothing was committed, so the already-written photos are orphans.
            await CleanupOrphanedPhotosAsync(preparedPhotos, cancellationToken);
            throw;
        }

        // A brand-new equipment cannot yet have an assignment, so custody is
        // unconditionally null here — no query needed.
        var photoSetRecord = photoSet is null ? null : await GetCurrentPhotoSetAsync(equipment.Id, cancellationToken);
        return ServiceResult.Ok(ToRecord(equipment, type.Name, spec, currentCustody: null, mobilePhoneSpecification: mobileSpec, currentPhotoSet: photoSetRecord));
    }

    /// <summary>
    /// The legacy backfill action — establishes the first current photo set
    /// for photo-required equipment that predates this feature. Rejects
    /// outright if a current set already exists (event C, Return physical
    /// receipt, is the only way to refresh an existing one).
    /// </summary>
    public async Task<ServiceResult<EquipmentRecord>> EstablishPhotosAsync(
        Guid id, IReadOnlyList<EquipmentPhotoFile> photos, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var equipment = await dbContext.Equipment.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (equipment is null)
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.NotFound, "Equipment not found.");
        }

        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);
        if (!EquipmentRules.RequiresConditionPhotos(type.Kind))
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.Validation, "Este tipo de equipamento não requer fotografias.");
        }

        var alreadyHasCurrent = await dbContext.EquipmentPhotoSets.AsNoTracking()
            .AnyAsync(s => s.EquipmentId == id && s.Status == PhotoSetStatus.Current, cancellationToken);
        if (alreadyHasCurrent)
        {
            return ServiceResult.Fail<EquipmentRecord>(
                ServiceErrorCode.PhotoSetAlreadyExists, "Este equipamento já tem um conjunto de fotografias atuais; utilize o fluxo de Devolução para o atualizar.");
        }

        if (photos.Count != RequiredPhotoCount)
        {
            return ServiceResult.Fail<EquipmentRecord>(
                ServiceErrorCode.Validation, $"É necessário enviar exatamente {RequiredPhotoCount} fotografias do estado do equipamento.");
        }

        var preparedPhotos = new List<PreparedPhoto>();
        foreach (var photo in photos)
        {
            var (error, prepared) = await ValidateAndReadImageAsync(photo, cancellationToken);
            if (error is { } validationError)
            {
                await CleanupOrphanedPhotosAsync(preparedPhotos, cancellationToken);
                return ServiceResult.Fail<EquipmentRecord>(validationError.Code, validationError.Message);
            }

            preparedPhotos.Add(prepared!);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var photoSet = new EquipmentPhotoSet
        {
            Id = Guid.NewGuid(),
            EquipmentId = id,
            Status = PhotoSetStatus.Current,
            Source = PhotoSetSource.InitialRegistration,
            CreatedAtUtc = now,
            CreatedByUserId = actorUserId,
            CreatedByDisplayNameSnapshot = actorDisplayName,
        };
        dbContext.EquipmentPhotoSets.Add(photoSet);

        var sortOrder = 1;
        foreach (var photo in preparedPhotos)
        {
            dbContext.EquipmentPhotoSetPhotos.Add(new EquipmentPhotoSetPhoto
            {
                Id = Guid.NewGuid(),
                EquipmentPhotoSetId = photoSet.Id,
                StorageKey = photo.StorageKey,
                OriginalFileName = photo.OriginalFileName,
                ContentType = photo.ContentType,
                SizeBytes = photo.SizeBytes,
                Sha256Hash = photo.Sha256,
                SortOrder = sortOrder++,
                UploadedAtUtc = now,
                UploadedByUserId = actorUserId,
            });
        }

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            await CleanupOrphanedPhotosAsync(preparedPhotos, cancellationToken);
            return ServiceResult.Fail<EquipmentRecord>(
                ServiceErrorCode.PhotoSetAlreadyExists, "Este equipamento já tem um conjunto de fotografias atuais.");
        }

        try
        {
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentPhotoSet.Established,
                EntityType: "Equipment",
                EntityId: equipment.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { equipment.AssetTag, PhotoSetId = photoSet.Id, Source = photoSet.Source.ToString() }), cancellationToken);
            await transaction.CommitIfOwnedAsync(cancellationToken);
        }
        catch
        {
            await CleanupOrphanedPhotosAsync(preparedPhotos, cancellationToken);
            throw;
        }

        var spec = await dbContext.LaptopSpecifications.AsNoTracking().FirstOrDefaultAsync(s => s.EquipmentId == id, cancellationToken);
        var mobileSpec = await dbContext.MobilePhoneSpecifications.AsNoTracking().FirstOrDefaultAsync(s => s.EquipmentId == id, cancellationToken);
        var custody = await GetCurrentCustodyAsync(id, cancellationToken);
        var establishedPhotoSet = await GetCurrentPhotoSetAsync(id, cancellationToken);
        return ServiceResult.Ok(ToRecord(equipment, type.Name, spec, custody, mobilePhoneSpecification: mobileSpec, currentPhotoSet: establishedPhotoSet));
    }

    public async Task<ServiceResult<EquipmentRecord>> UpdateAsync(
        Guid id, UpdateEquipmentData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var equipment = await dbContext.Equipment.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (equipment is null)
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.NotFound, "Equipment not found.");
        }

        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);

        var validation = ValidateAgainstKind(type.Kind, data.Manufacturer, data.SerialNumber, data.LaptopSpecification, data.MobilePhoneSpecification);
        if (validation is not null)
        {
            return ServiceResult.Fail<EquipmentRecord>(validation.Value.Code, validation.Value.Message);
        }

        var normalizedSerial = EquipmentRules.NormalizeSerialNumber(data.SerialNumber);
        if (normalizedSerial is not null && normalizedSerial != equipment.NormalizedSerialNumber &&
            await dbContext.Equipment.AnyAsync(e => e.NormalizedSerialNumber == normalizedSerial && e.Id != id, cancellationToken))
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.DuplicateSerialNumber, "Equipment with this serial number already exists.");
        }

        var normalizedImei = type.Kind == EquipmentTypeKind.MobilePhone
            ? MobilePhoneSpecificationRules.NormalizeImei(data.MobilePhoneSpecification!.Imei)
            : null;
        if (normalizedImei is not null &&
            await dbContext.MobilePhoneSpecifications.AnyAsync(s => s.NormalizedImei == normalizedImei && s.EquipmentId != id, cancellationToken))
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.DuplicateImei, DuplicateImeiMessage);
        }

        var changedFields = new List<string>();
        if (!string.Equals(equipment.Manufacturer, Trim(data.Manufacturer), StringComparison.Ordinal))
        {
            changedFields.Add("manufacturer");
        }

        if (!string.Equals(equipment.Model, Trim(data.Model), StringComparison.Ordinal))
        {
            changedFields.Add("model");
        }

        if (!string.Equals(equipment.SerialNumber, Trim(data.SerialNumber), StringComparison.Ordinal))
        {
            changedFields.Add("serialNumber");
        }

        if (!string.Equals(equipment.Notes, Trim(data.Notes), StringComparison.Ordinal))
        {
            changedFields.Add("notes");
        }

        if (equipment.PurchaseDate != data.PurchaseDate)
        {
            changedFields.Add("purchaseDate");
        }

        if (!string.Equals(equipment.InvoiceNumber, Trim(data.InvoiceNumber), StringComparison.Ordinal))
        {
            changedFields.Add("invoiceNumber");
        }

        if (!string.Equals(equipment.PurchaseVendor, Trim(data.PurchaseVendor), StringComparison.Ordinal))
        {
            changedFields.Add("purchaseVendor");
        }

        equipment.Manufacturer = Trim(data.Manufacturer);
        equipment.Model = Trim(data.Model);
        equipment.SerialNumber = Trim(data.SerialNumber);
        equipment.NormalizedSerialNumber = normalizedSerial;
        equipment.Notes = Trim(data.Notes);
        equipment.PurchaseDate = data.PurchaseDate;
        equipment.InvoiceNumber = Trim(data.InvoiceNumber);
        equipment.PurchaseVendor = Trim(data.PurchaseVendor);
        equipment.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        LaptopSpecification? spec = null;
        if (type.Kind == EquipmentTypeKind.Laptop)
        {
            spec = await dbContext.LaptopSpecifications.FirstOrDefaultAsync(s => s.EquipmentId == id, cancellationToken);
            var newProcessor = data.LaptopSpecification!.Processor.Trim();
            var newMemory = data.LaptopSpecification.Memory.Trim();
            var newStorage = data.LaptopSpecification.Storage.Trim();

            if (spec is null)
            {
                spec = new LaptopSpecification { EquipmentId = id, Processor = newProcessor, Memory = newMemory, Storage = newStorage };
                dbContext.LaptopSpecifications.Add(spec);
                changedFields.Add("laptopSpecification");
            }
            else if (spec.Processor != newProcessor || spec.Memory != newMemory || spec.Storage != newStorage)
            {
                spec.Processor = newProcessor;
                spec.Memory = newMemory;
                spec.Storage = newStorage;
                changedFields.Add("laptopSpecification");
            }
        }

        MobilePhoneSpecification? mobileSpec = null;
        if (type.Kind == EquipmentTypeKind.MobilePhone)
        {
            mobileSpec = await dbContext.MobilePhoneSpecifications.FirstOrDefaultAsync(s => s.EquipmentId == id, cancellationToken);
            var newImei = data.MobilePhoneSpecification!.Imei.Trim();
            var newMobileNumber = Trim(data.MobilePhoneSpecification.MobileNumber);
            var newSimSerialNumber = Trim(data.MobilePhoneSpecification.SimSerialNumber);

            if (mobileSpec is null)
            {
                mobileSpec = new MobilePhoneSpecification { EquipmentId = id, Imei = newImei, NormalizedImei = normalizedImei!, MobileNumber = newMobileNumber, SimSerialNumber = newSimSerialNumber };
                dbContext.MobilePhoneSpecifications.Add(mobileSpec);
                changedFields.Add("mobilePhoneSpecification");
            }
            else if (mobileSpec.Imei != newImei || mobileSpec.MobileNumber != newMobileNumber || mobileSpec.SimSerialNumber != newSimSerialNumber)
            {
                mobileSpec.Imei = newImei;
                mobileSpec.NormalizedImei = normalizedImei!;
                mobileSpec.MobileNumber = newMobileNumber;
                mobileSpec.SimSerialNumber = newSimSerialNumber;
                changedFields.Add("mobilePhoneSpecification");
            }
        }

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolationOn(ex, SerialNumberConstraintName))
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.DuplicateSerialNumber, "Equipment with this serial number already exists.");
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolationOn(ex, ImeiConstraintName))
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.DuplicateImei, DuplicateImeiMessage);
        }

        if (changedFields.Count > 0)
        {
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.Equipment.Updated,
                EntityType: "Equipment",
                EntityId: equipment.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                Metadata: new { equipment.AssetTag, changedFields }), cancellationToken);
        }

        await transaction.CommitIfOwnedAsync(cancellationToken);

        var custody = await GetCurrentCustodyAsync(id, cancellationToken);
        var pendingRequest = await GetPendingRetirementRequestAsync(id, cancellationToken);
        var photoSet = await GetCurrentPhotoSetAsync(id, cancellationToken);
        return ServiceResult.Ok(ToRecord(equipment, type.Name, spec, custody, pendingRequest, mobileSpec, photoSet));
    }

    public async Task<ServiceResult<EquipmentRecord>> RetireAsync(
        Guid id, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        Equipment? equipment;

        // The eligibility read and the write share one Serializable snapshot:
        // Equipment has no concurrency token, so a status read before the
        // transaction (or under Read Committed) would let this UPDATE silently
        // overwrite a dispatch or Confirmar entrega that committed in between
        // (an InTransit/Assigned item becoming Retired). Here such a commit is
        // either seen (not eligible) or makes the write fail with a
        // serialization conflict. Joins ApproveAsync's transaction (already
        // Serializable) when called from there; that owner then translates the
        // conflict at its own boundary.
        await using (var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(System.Data.IsolationLevel.Serializable, cancellationToken))
        {
            try
            {
                equipment = await dbContext.Equipment.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
                if (equipment is null)
                {
                    return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.NotFound, "Equipment not found.");
                }

                if (!EquipmentLifecycleRules.CanRetire(equipment.Status))
                {
                    return ServiceResult.Fail<EquipmentRecord>(
                        ServiceErrorCode.InvalidLifecycleTransition,
                        $"Equipment in status '{equipment.Status}' cannot be retired.");
                }

                equipment.Status = EquipmentStatus.Retired;
                equipment.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

                // Resolve any orphaned Pending retirement request(s) for this
                // equipment as part of the SAME write — retired equipment must never
                // retain an unresolved Pending request, regardless of whether
                // retirement was triggered by Administration's direct action here or
                // by EquipmentRetirementRequestService.ApproveAsync delegating into
                // this exact method (see the Equipment Retirement Request workflow's
                // invariant). At most one such row can exist (partial unique index),
                // but this resolves every match rather than assuming that.
                var pendingRequests = await dbContext.EquipmentRetirementRequests
                    .Where(r => r.EquipmentId == id && r.Status == EquipmentRetirementRequestStatus.Pending)
                    .ToListAsync(cancellationToken);
                foreach (var pending in pendingRequests)
                {
                    pending.Status = EquipmentRetirementRequestStatus.Approved;
                    pending.DecidedByUserId = actorUserId;
                    pending.DecidedByDisplayNameSnapshot = actorDisplayName;
                    pending.DecidedAtUtc = equipment.UpdatedAtUtc;
                    pending.UpdatedAtUtc = equipment.UpdatedAtUtc;
                }

                await dbContext.SaveChangesAsync(cancellationToken);

                await auditService.RecordAsync(new AuditEntry(
                    AuditActions.Equipment.Retired,
                    EntityType: "Equipment",
                    EntityId: equipment.Id.ToString(),
                    ActorUserId: actorUserId,
                    ActorDisplayNameSnapshot: actorDisplayName,
                    NewValues: new { equipment.AssetTag }), cancellationToken);

                foreach (var pending in pendingRequests)
                {
                    await auditService.RecordAsync(new AuditEntry(
                        AuditActions.EquipmentRetirementRequest.Approved,
                        EntityType: "Equipment",
                        EntityId: equipment.Id.ToString(),
                        ActorUserId: actorUserId,
                        ActorDisplayNameSnapshot: actorDisplayName,
                        NewValues: new { equipment.AssetTag, RetirementRequestId = pending.Id }), cancellationToken);
                }

                await transaction.CommitIfOwnedAsync(cancellationToken);
            }
            catch (Exception ex) when (transaction is not null && PostgresConflictClassifier.IsSerializationFailure(ex))
            {
                // Only when this call owns the transaction; nothing was committed.
                dbContext.ChangeTracker.Clear();
                return ServiceResult.Fail<EquipmentRecord>(
                    ServiceErrorCode.Concurrency, "Este equipamento foi alterado por outra operação. Atualize a página e tente novamente.");
            }
        }

        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);
        var spec = await dbContext.LaptopSpecifications.AsNoTracking().FirstOrDefaultAsync(s => s.EquipmentId == id, cancellationToken);
        // Retirement is only reachable from Available/Damaged (see
        // EquipmentLifecycleRules.CanRetire), so no active assignment can exist
        // here — but we still resolve it through the real source of truth
        // rather than hard-coding null, so this stays correct if that ever changes.
        var custody = await GetCurrentCustodyAsync(id, cancellationToken);
        return ServiceResult.Ok(ToRecord(equipment, type.Name, spec, custody, pendingRetirementRequest: null));
    }

    private static (ServiceErrorCode Code, string Message)? ValidateAgainstKind(
        EquipmentTypeKind kind, string? manufacturer, string? serialNumber, LaptopSpecificationRecord? laptopSpecification,
        MobilePhoneSpecificationRecord? mobilePhoneSpecification = null)
    {
        if (kind == EquipmentTypeKind.Laptop)
        {
            if (EquipmentRules.IsBlank(manufacturer))
            {
                return (ServiceErrorCode.Validation, "Manufacturer is required for laptops.");
            }

            if (EquipmentRules.IsBlank(serialNumber))
            {
                return (ServiceErrorCode.Validation, "Serial number is required for laptops.");
            }

            if (laptopSpecification is null ||
                !LaptopSpecificationRules.IsComplete(laptopSpecification.Processor, laptopSpecification.Memory, laptopSpecification.Storage))
            {
                return (ServiceErrorCode.Validation, "Processor, memory and storage are required for laptops.");
            }
        }
        else if (laptopSpecification is not null)
        {
            return (ServiceErrorCode.Validation, "Only laptop equipment types accept a laptop specification.");
        }

        if (kind == EquipmentTypeKind.MobilePhone)
        {
            if (EquipmentRules.IsBlank(manufacturer))
            {
                return (ServiceErrorCode.Validation, "Manufacturer is required for mobile phones.");
            }

            if (mobilePhoneSpecification is null || !MobilePhoneSpecificationRules.IsComplete(mobilePhoneSpecification.Imei))
            {
                return (ServiceErrorCode.Validation, RequiredImeiMessage);
            }

            if (!MobilePhoneSpecificationRules.IsValidImei(mobilePhoneSpecification.Imei))
            {
                return (ServiceErrorCode.Validation, InvalidImeiMessage);
            }
        }
        else if (mobilePhoneSpecification is not null)
        {
            return (ServiceErrorCode.Validation, "Only mobile phone equipment types accept a mobile phone specification.");
        }

        return null;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// "Alterar fotografias" — Administration's correction of the CURRENT
    /// condition photos outside custody (see
    /// <see cref="EquipmentRules.ResolveCurrentPhotoCorrection"/>). Never
    /// mutates or deletes the current set: it becomes Historical (so every
    /// Responsibility Term or Handover that froze it keeps resolving it) and a
    /// complete new set of exactly 3 becomes Current. Replaced slots are new
    /// uploads; every other slot is copied to a new private object, so no two
    /// sets ever share a file. The new set keeps the corrected set's origin
    /// (InitialRegistration, or Return with the same EquipmentReturnId) — it is
    /// a correction of that set, not a new business event; the audit records
    /// the replacement. Eligibility and the expected current set are re-checked
    /// inside a serializable transaction; any failure leaves the previous set
    /// current and removes every file this call wrote.
    /// </summary>
    public async Task<ServiceResult<EquipmentRecord>> ReplaceCurrentPhotosAsync(
        Guid id, ReplaceCurrentPhotosData data, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var equipment = await dbContext.Equipment.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (equipment is null)
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.NotFound, "Equipment not found.");
        }

        var type = await dbContext.EquipmentTypes.AsNoTracking().FirstAsync(t => t.Id == equipment.EquipmentTypeId, cancellationToken);
        if (!EquipmentRules.RequiresConditionPhotos(type.Kind))
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.Validation, "Este tipo de equipamento não requer fotografias.");
        }

        if (data.ReplacementsBySlot.Count == 0 || data.ReplacementsBySlot.Keys.Any(slot => slot is < 1 or > RequiredPhotoCount))
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.Validation, "Selecione pelo menos uma fotografia para substituir.");
        }

        var currentSet = await dbContext.EquipmentPhotoSets.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EquipmentId == id && s.Status == PhotoSetStatus.Current, cancellationToken);
        if (currentSet is null)
        {
            return ServiceResult.Fail<EquipmentRecord>(
                ServiceErrorCode.InvalidLifecycleTransition, "Este equipamento ainda não tem fotografias atuais; registe-as primeiro.");
        }

        if (currentSet.Id != data.ExpectedPhotoSetId)
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.Concurrency, StalePhotoSetMessage);
        }

        var eligibilityFailure = await CheckPhotoCorrectionEligibilityAsync(equipment.Id, equipment.Status, cancellationToken);
        if (eligibilityFailure is not null)
        {
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.InvalidLifecycleTransition, eligibilityFailure);
        }

        var currentPhotos = await dbContext.EquipmentPhotoSetPhotos.AsNoTracking()
            .Where(p => p.EquipmentPhotoSetId == currentSet.Id)
            .OrderBy(p => p.SortOrder)
            .ToListAsync(cancellationToken);
        if (currentPhotos.Count != RequiredPhotoCount)
        {
            return ServiceResult.Fail<EquipmentRecord>(
                ServiceErrorCode.InvalidLifecycleTransition, "O conjunto de fotografias atual está incompleto e não pode ser corrigido.");
        }

        // Files first (outside any transaction, ADR-033): validated uploads for
        // the replaced slots, private copies for the kept ones. Every key
        // written is tracked so a failure anywhere removes them all.
        var newPhotos = new List<PreparedPhoto>();
        var writtenKeys = new List<string>();
        try
        {
            foreach (var existing in currentPhotos)
            {
                if (data.ReplacementsBySlot.TryGetValue(existing.SortOrder, out var replacement))
                {
                    var (error, prepared) = await ValidateAndReadImageAsync(replacement, cancellationToken);
                    if (error is { } validationError)
                    {
                        await DeleteKeysAsync(writtenKeys, cancellationToken);
                        return ServiceResult.Fail<EquipmentRecord>(validationError.Code, validationError.Message);
                    }

                    writtenKeys.Add(prepared!.StorageKey);
                    newPhotos.Add(prepared);
                }
                else
                {
                    var copy = await CopyPhotoAsync(existing, cancellationToken);
                    if (copy is null)
                    {
                        await DeleteKeysAsync(writtenKeys, cancellationToken);
                        return ServiceResult.Fail<EquipmentRecord>(
                            ServiceErrorCode.InvalidLifecycleTransition, "Uma das fotografias atuais não foi encontrada no armazenamento; substitua as 3 fotografias.");
                    }

                    writtenKeys.Add(copy.StorageKey);
                    newPhotos.Add(copy);
                }
            }
        }
        catch
        {
            await DeleteKeysAsync(writtenKeys, cancellationToken);
            throw;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var newSet = new EquipmentPhotoSet
        {
            Id = Guid.NewGuid(),
            EquipmentId = id,
            Status = PhotoSetStatus.Current,
            Source = currentSet.Source,
            EquipmentReturnId = currentSet.EquipmentReturnId,
            CreatedAtUtc = now,
            CreatedByUserId = actorUserId,
            CreatedByDisplayNameSnapshot = actorDisplayName,
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            // Re-checked at mutation time: an assignment, a registered
            // dispatch or another photo change since the checks above must
            // win. The equipment row is written too, so a concurrent
            // "Confirmar entrega" (which also writes it) conflicts.
            var trackedEquipment = await dbContext.Equipment.FirstAsync(e => e.Id == id, cancellationToken);
            var trackedCurrent = await dbContext.EquipmentPhotoSets
                .FirstOrDefaultAsync(s => s.EquipmentId == id && s.Status == PhotoSetStatus.Current, cancellationToken);
            if (trackedCurrent?.Id != currentSet.Id)
            {
                await DeleteKeysAsync(writtenKeys, cancellationToken);
                return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.Concurrency, StalePhotoSetMessage);
            }

            var lateFailure = await CheckPhotoCorrectionEligibilityAsync(id, trackedEquipment.Status, cancellationToken);
            if (lateFailure is not null)
            {
                await DeleteKeysAsync(writtenKeys, cancellationToken);
                return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.InvalidLifecycleTransition, lateFailure);
            }

            trackedCurrent.Status = PhotoSetStatus.Historical;
            trackedEquipment.UpdatedAtUtc = now;
            dbContext.EquipmentPhotoSets.Add(newSet);

            var sortOrder = 1;
            foreach (var photo in newPhotos)
            {
                dbContext.EquipmentPhotoSetPhotos.Add(new EquipmentPhotoSetPhoto
                {
                    Id = Guid.NewGuid(),
                    EquipmentPhotoSetId = newSet.Id,
                    StorageKey = photo.StorageKey,
                    OriginalFileName = photo.OriginalFileName,
                    ContentType = photo.ContentType,
                    SizeBytes = photo.SizeBytes,
                    Sha256Hash = photo.Sha256,
                    SortOrder = sortOrder++,
                    UploadedAtUtc = now,
                    UploadedByUserId = actorUserId,
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentPhotoSet.Replaced,
                EntityType: "Equipment",
                EntityId: id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                OldValues: new { PhotoSetId = currentSet.Id },
                NewValues: new
                {
                    trackedEquipment.AssetTag,
                    PhotoSetId = newSet.Id,
                    Source = newSet.Source.ToString(),
                    ReplacedSlots = data.ReplacementsBySlot.Keys.Order().ToList(),
                }), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.Find(
            ex, PostgresErrorCodes.SerializationFailure, PostgresErrorCodes.UniqueViolation) is not null)
        {
            dbContext.ChangeTracker.Clear();
            await DeleteKeysAsync(writtenKeys, cancellationToken);
            return ServiceResult.Fail<EquipmentRecord>(ServiceErrorCode.Concurrency, StalePhotoSetMessage);
        }
        catch
        {
            await DeleteKeysAsync(writtenKeys, cancellationToken);
            throw;
        }

        var record = await GetByIdAsync(id, cancellationToken);
        return ServiceResult.Ok(record!);
    }

    private const string StalePhotoSetMessage =
        "As fotografias ou o estado deste equipamento foram alterados por outra operação. Atualize a página e tente novamente.";

    /// <summary>Null when correction is allowed; otherwise the pt-PT reason. Custody is the Active assignment, never the status label.</summary>
    private async Task<string?> CheckPhotoCorrectionEligibilityAsync(Guid equipmentId, EquipmentStatus status, CancellationToken cancellationToken) =>
        await ResolvePhotoCorrectionAsync(equipmentId, status, cancellationToken) switch
        {
            CurrentPhotoCorrectionEligibility.Eligible => null,
            CurrentPhotoCorrectionEligibility.InCustody =>
                "Este equipamento está atribuído a um consultor. As fotografias atuais só podem ser alteradas fora de custódia.",
            CurrentPhotoCorrectionEligibility.InDispatchedHandover =>
                "Este equipamento faz parte de uma entrega com envio registado. As fotografias atuais não podem ser alteradas até a entrega ser confirmada.",
            _ => "Um equipamento abatido não pode ter as fotografias alteradas.",
        };

    private async Task<CurrentPhotoCorrectionEligibility> ResolvePhotoCorrectionAsync(
        Guid equipmentId, EquipmentStatus status, CancellationToken cancellationToken)
    {
        var hasActiveCustody = await dbContext.EquipmentAssignments.AsNoTracking()
            .AnyAsync(a => a.EquipmentId == equipmentId && a.Status == AssignmentStatus.Active, cancellationToken);
        var isInDispatchedHandover = await (
                from i in dbContext.EquipmentHandoverItems.AsNoTracking()
                join h in dbContext.EquipmentHandovers.AsNoTracking() on i.EquipmentHandoverId equals h.Id
                where i.EquipmentId == equipmentId && h.Status == HandoverStatus.Draft
                      && dbContext.EquipmentDispatches.Active().Any(d => d.EquipmentHandoverId == h.Id)
                select i.Id)
            .AnyAsync(cancellationToken);

        return EquipmentRules.ResolveCurrentPhotoCorrection(status, hasActiveCustody, isInDispatchedHandover);
    }

    /// <summary>A kept slot's image copied to a brand-new private object — the historical set keeps its own file, untouched, so neither set can ever lose a file through the other.</summary>
    private async Task<PreparedPhoto?> CopyPhotoAsync(EquipmentPhotoSetPhoto existing, CancellationToken cancellationToken)
    {
        await using var source = await fileStorage.OpenReadAsync(existing.StorageKey, cancellationToken);
        if (source is null)
        {
            return null;
        }

        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var detected = ImageSignatureDetector.Detect(bytes);
        if (detected is null)
        {
            return null;
        }

        var storageKey = BuildStorageKey(detected.Value);
        buffer.Position = 0;
        await fileStorage.SaveAsync(storageKey, buffer, cancellationToken);

        return new PreparedPhoto(
            storageKey, existing.OriginalFileName, existing.ContentType, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private async Task DeleteKeysAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        foreach (var key in keys)
        {
            await fileStorage.DeleteAsync(key, cancellationToken);
        }
    }

    private const int RequiredPhotoCount = 3;

    private sealed record PreparedPhoto(string StorageKey, string OriginalFileName, string ContentType, long SizeBytes, string Sha256);

    /// <summary>Validates the real image signature (never the declared Content-Type alone), then saves it to private storage — same discipline as <c>EquipmentHandoverService.UploadPhotoAsync</c>/<c>EquipmentReturnService</c>'s photo uploads.</summary>
    private async Task<((ServiceErrorCode Code, string Message)? Error, PreparedPhoto? Prepared)> ValidateAndReadImageAsync(
        EquipmentPhotoFile photo, CancellationToken cancellationToken)
    {
        var maxSize = storageOptions.Value.MaxPhotoSizeBytes;
        await using var buffer = new MemoryStream();
        await photo.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > maxSize)
        {
            return ((ServiceErrorCode.FileTooLarge, $"Photo must be between 1 byte and {maxSize} bytes."), null);
        }

        var bytes = buffer.ToArray();
        var detected = ImageSignatureDetector.Detect(bytes);
        if (detected is null || !ImageSignatureDetector.MatchesDeclaredContentType(detected.Value, photo.DeclaredContentType))
        {
            return ((ServiceErrorCode.InvalidFile, "Only genuine JPEG, PNG or WebP images are accepted."), null);
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var storageKey = BuildStorageKey(detected.Value);

        buffer.Position = 0;
        await fileStorage.SaveAsync(storageKey, buffer, cancellationToken);

        return (null, new PreparedPhoto(storageKey, Trim(photo.OriginalFileName) ?? "photo", photo.DeclaredContentType, bytes.LongLength, sha256));
    }

    private async Task CleanupOrphanedPhotosAsync(IReadOnlyList<PreparedPhoto> preparedPhotos, CancellationToken cancellationToken)
    {
        foreach (var photo in preparedPhotos)
        {
            await fileStorage.DeleteAsync(photo.StorageKey, cancellationToken);
        }
    }

    private static string BuildStorageKey(ImageContentType type)
    {
        var extension = type switch
        {
            ImageContentType.Jpeg => "jpg",
            ImageContentType.Png => "png",
            ImageContentType.WebP => "webp",
            _ => "bin",
        };

        return $"equipment-photos/{Guid.NewGuid():N}.{extension}";
    }

    /// <summary>Validates the full equipment/photo-set/photo chain, not just that the photo id exists somewhere — same defense-in-depth discipline as <c>EquipmentHandoverService.GetPhotoContentAsync</c>.</summary>
    public async Task<(Stream Content, string ContentType, string OriginalFileName)?> GetPhotoContentAsync(
        Guid equipmentId, Guid photoId, CancellationToken cancellationToken = default)
    {
        var photo = await (
                from p in dbContext.EquipmentPhotoSetPhotos.AsNoTracking()
                join s in dbContext.EquipmentPhotoSets.AsNoTracking() on p.EquipmentPhotoSetId equals s.Id
                where p.Id == photoId && s.EquipmentId == equipmentId
                select p)
            .FirstOrDefaultAsync(cancellationToken);
        if (photo is null)
        {
            return null;
        }

        var stream = await fileStorage.OpenReadAsync(photo.StorageKey, cancellationToken);
        return stream is null ? null : (stream, photo.ContentType, photo.OriginalFileName);
    }

    private async Task<EquipmentPhotoSetRecord?> GetCurrentPhotoSetAsync(Guid equipmentId, CancellationToken cancellationToken)
    {
        var photoSet = await dbContext.EquipmentPhotoSets.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EquipmentId == equipmentId && s.Status == PhotoSetStatus.Current, cancellationToken);
        if (photoSet is null)
        {
            return null;
        }

        var photos = await dbContext.EquipmentPhotoSetPhotos.AsNoTracking()
            .Where(p => p.EquipmentPhotoSetId == photoSet.Id)
            .OrderBy(p => p.SortOrder)
            .Select(p => new EquipmentPhotoRecord(p.Id, p.OriginalFileName, p.ContentType, p.SizeBytes, p.SortOrder, p.UploadedAtUtc))
            .ToListAsync(cancellationToken);

        return new EquipmentPhotoSetRecord(photoSet.Id, photoSet.Source, photoSet.CreatedAtUtc, photos);
    }

    /// <summary>
    /// Resolves current custody exclusively from the Active-status
    /// EquipmentAssignment row for this equipment (docs/PROJECT_SPEC.md,
    /// section 2) — never from Equipment.Status. Regime/Empresa/Função come
    /// from the consultant's current allocation, if one still exists at read
    /// time; their absence does not affect custody itself.
    /// </summary>
    private async Task<EquipmentCurrentCustodyRecord?> GetCurrentCustodyAsync(Guid equipmentId, CancellationToken cancellationToken)
    {
        var assignment = await dbContext.EquipmentAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.EquipmentId == equipmentId && a.Status == AssignmentStatus.Active, cancellationToken);
        if (assignment is null)
        {
            return null;
        }

        var consultant = await dbContext.Consultants.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == assignment.ConsultantId, cancellationToken);
        if (consultant is null)
        {
            return null;
        }

        var currentAllocation = await dbContext.ConsultantAllocations.AsNoTracking()
            .Where(a => a.ConsultantId == assignment.ConsultantId && a.Status == AllocationStatus.Active)
            .OrderByDescending(a => a.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        string? companyName = null;
        if (currentAllocation?.CompanyId is { } companyId)
        {
            companyName = await dbContext.Companies.AsNoTracking()
                .Where(c => c.Id == companyId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new EquipmentCurrentCustodyRecord(
            assignment.ConsultantId, consultant.FullName, currentAllocation?.WorkforceType, currentAllocation?.EngagementType,
            companyName, currentAllocation?.RoleOrFunction, assignment.StartedAtUtc, assignment.EquipmentHandoverId, assignment.Origin);
    }

    /// <summary>Embeds the equipment's currently-Pending retirement request, if any, on the detail projection (see the retirement-request workflow) — deliberately minimal, no account/user data beyond the requester's display name.</summary>
    private async Task<EquipmentPendingRetirementRequestRecord?> GetPendingRetirementRequestAsync(Guid equipmentId, CancellationToken cancellationToken)
    {
        var pending = await dbContext.EquipmentRetirementRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EquipmentId == equipmentId && r.Status == EquipmentRetirementRequestStatus.Pending, cancellationToken);

        return pending is null
            ? null
            : new EquipmentPendingRetirementRequestRecord(pending.Id, pending.RequestedByDisplayNameSnapshot, pending.RequestedAtUtc, pending.RequestReason);
    }

    private static EquipmentRecord ToRecord(
        Domain.Equipments.Equipment equipment, string typeName, LaptopSpecification? spec, EquipmentCurrentCustodyRecord? currentCustody,
        EquipmentPendingRetirementRequestRecord? pendingRetirementRequest = null, MobilePhoneSpecification? mobilePhoneSpecification = null,
        EquipmentPhotoSetRecord? currentPhotoSet = null, bool currentPhotosCorrectable = false) => new(
        equipment.Id, equipment.EquipmentTypeId, typeName, equipment.AssetTag, equipment.Manufacturer, equipment.Model,
        equipment.SerialNumber, equipment.Notes, equipment.Status, equipment.CreatedAtUtc, equipment.UpdatedAtUtc,
        spec is null ? null : new LaptopSpecificationRecord(spec.Processor, spec.Memory, spec.Storage), currentCustody,
        equipment.PurchaseDate, equipment.InvoiceNumber, equipment.PurchaseVendor, pendingRetirementRequest,
        mobilePhoneSpecification is null ? null : new MobilePhoneSpecificationRecord(mobilePhoneSpecification.Imei, mobilePhoneSpecification.MobileNumber, mobilePhoneSpecification.SimSerialNumber),
        currentPhotoSet,
        currentPhotosCorrectable);
}
