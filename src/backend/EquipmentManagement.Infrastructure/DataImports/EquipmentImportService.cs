using System.Text.Json;
using ClosedXML.Excel;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Equipments;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Equipment Excel import: template generation, preview, and confirmation
/// (Phase 8 scope). Reuses <see cref="EquipmentRules"/>/
/// <see cref="LaptopSpecificationRules"/> (via <see cref="EquipmentImportRowValidator"/>)
/// for validation and <c>EquipmentService.CreateAsync</c> itself for
/// persistence — including its own <c>AssetTagGenerator</c> call and audit
/// entry — since, unlike allocation creation, it opens no transaction of its
/// own and can safely participate in the batch confirmation's ambient one.
/// The workbook and every message are pt-PT; columns are read by their
/// internal field key, whichever accepted header the file uses (ADR-070).
/// </summary>
public sealed class EquipmentImportService(
    ApplicationDbContext dbContext,
    ExcelWorkbookReader excelReader,
    EquipmentService equipmentService,
    Auditing.AuditService auditService,
    TimeProvider timeProvider)
{
    private static readonly IReadOnlyList<ImportColumn> Columns = EquipmentImportColumns.All;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The pt-PT template (ADR-070): an empty, ready-to-fill "Equipamentos"
    /// sheet plus an "Instruções" sheet. The example lives on the
    /// instructions sheet, never as a data row that would be imported if
    /// left in place.
    /// </summary>
    public static byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Equipamentos");

        // Only the equipment type must be filled in on every row; the strong
        // header marks exactly that, the rest depend on the type (see Instruções).
        ExcelSheetLayout.WriteHeaderRow(sheet, [.. Columns.Select(c => c.Header)], i => Columns[i] == EquipmentImportColumns.EquipmentType);
        ExcelSheetLayout.SetColumnWidths(sheet, [26, 22, 26, 26, 40, 28, 18, 20]);

        // Text, so Excel never turns a serial number into a number and drops
        // its leading zeros (Phase 8 scope).
        sheet.Column(IndexOf(EquipmentImportColumns.SerialNumber)).Style.NumberFormat.Format = ExcelSheetLayout.TextFormat;

        string laptopOnly = "Apenas para portáteis";
        ExcelSheetLayout.AddInstructionsSheet(
            workbook,
            "Importação de equipamentos",
            [
                "Preencha a folha \"Equipamentos\" a partir da linha 2: uma linha por equipamento. Não altere o nome das colunas.",
                "O código do equipamento é gerado automaticamente e o equipamento fica no estado Disponível: nenhum dos dois é indicado neste ficheiro.",
                "A atribuição a um consultor não é importada: é sempre feita na aplicação, através de uma entrega.",
                "Um número de série repetido no ficheiro, ou já existente na aplicação, é assinalado como erro.",
                "Os tipos de equipamento da categoria Telemóvel não podem ser importados por este ficheiro: o IMEI é obrigatório e só é registado na aplicação.",
                "Nada é gravado antes de confirmar a importação na aplicação. Se alguma linha tiver erros, nenhuma linha é importada.",
                "Não inclua PIN, PUK, palavras-passe ou outros dados confidenciais.",
                "No cabeçalho da folha \"Equipamentos\", a cor mais escura assinala o campo que é sempre obrigatório.",
            ],
            "Colunas da folha \"Equipamentos\"",
            [
                new(EquipmentImportColumns.EquipmentType.Header, "Sim", "Nome de um tipo de equipamento já existente e ativo na aplicação (menu \"Tipos de equipamento\"). Os tipos não são criados pela importação.", "Portátil"),
                new(EquipmentImportColumns.Manufacturer.Header, laptopOnly, "Fabricante do equipamento.", "Dell"),
                new(EquipmentImportColumns.Model.Header, "Não", "Modelo do equipamento.", "Latitude 5440"),
                new(EquipmentImportColumns.SerialNumber.Header, laptopOnly, "Mantenha a coluna formatada como Texto, para preservar os zeros à esquerda.", "0000000EXEMPLO"),
                new(EquipmentImportColumns.Notes.Header, "Não", "Informação adicional sobre o equipamento.", string.Empty),
                new(EquipmentImportColumns.Processor.Header, laptopOnly, "Deixe vazio nos equipamentos que não são portáteis.", "Intel Core i5-1345U"),
                new(EquipmentImportColumns.Memory.Header, laptopOnly, "Deixe vazio nos equipamentos que não são portáteis.", "16 GB"),
                new(EquipmentImportColumns.Storage.Header, laptopOnly, "Deixe vazio nos equipamentos que não são portáteis.", "512 GB SSD"),
            ]);

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    /// <summary>1-based worksheet column of an import column in a generated template.</summary>
    private static int IndexOf(ImportColumn column) => Columns.ToList().IndexOf(column) + 1;

    private static readonly string DuplicateSerialInFileMessage =
        $"O valor indicado em \"{EquipmentImportColumns.SerialNumber.Header}\" aparece mais do que uma vez no ficheiro.";

    private static readonly string DuplicateSerialExistingMessage =
        $"Já existe um equipamento com o valor indicado em \"{EquipmentImportColumns.SerialNumber.Header}\".";

    private static readonly string UnknownEquipmentTypeMessage =
        $"O valor indicado em \"{EquipmentImportColumns.EquipmentType.Header}\" não é válido: não existe nenhum tipo de equipamento com este nome.";

    private static readonly string InactiveEquipmentTypeMessage =
        $"O tipo de equipamento indicado em \"{EquipmentImportColumns.EquipmentType.Header}\" está inativo.";

    public async Task<ServiceResult<EquipmentImportPreviewRecord>> PreviewAsync(
        byte[] fileBytes, string fileName, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var opened = excelReader.OpenAndValidate(fileBytes, fileName);
        if (!opened.Succeeded)
        {
            return ServiceResult.Fail<EquipmentImportPreviewRecord>(opened.ErrorCode, opened.ErrorMessage!);
        }

        using var workbook = opened.Value.Workbook;
        var worksheet = opened.Value.Worksheet;

        var headerMap = ExcelWorkbookReader.MapHeaders(worksheet, Columns);
        if (!headerMap.Succeeded)
        {
            return ServiceResult.Fail<EquipmentImportPreviewRecord>(headerMap.ErrorCode, headerMap.ErrorMessage!);
        }

        var columns = headerMap.Value!;
        int? Col(ImportColumn column) => columns.TryGetValue(column.Field, out var idx) ? idx : null;

        var rows = new List<(int RowNumber, List<ImportIssue> Issues, EquipmentImportRowFields Fields)>();
        foreach (var row in ExcelWorkbookReader.GetDataRows(worksheet))
        {
            var formulaFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var column in Columns)
            {
                if (ExcelWorkbookReader.IsFormula(row, Col(column)))
                {
                    formulaFields.Add(column.Field);
                }
            }

            var equipmentType = excelReader.ReadText(row, Col(EquipmentImportColumns.EquipmentType), out var typeTooLong);
            var manufacturer = excelReader.ReadText(row, Col(EquipmentImportColumns.Manufacturer), out var manufacturerTooLong);
            var model = excelReader.ReadText(row, Col(EquipmentImportColumns.Model), out var modelTooLong);
            var serial = excelReader.ReadIdentifierText(row, Col(EquipmentImportColumns.SerialNumber), out var serialAmbiguous, out var serialTooLong);
            var notes = excelReader.ReadText(row, Col(EquipmentImportColumns.Notes), out var notesTooLong);
            var processor = excelReader.ReadText(row, Col(EquipmentImportColumns.Processor), out var processorTooLong);
            var memory = excelReader.ReadText(row, Col(EquipmentImportColumns.Memory), out var memoryTooLong);
            var storage = excelReader.ReadText(row, Col(EquipmentImportColumns.Storage), out var storageTooLong);

            var issues = new List<ImportIssue>();
            AddTooLong(issues, EquipmentImportColumns.EquipmentType, typeTooLong);
            AddTooLong(issues, EquipmentImportColumns.Manufacturer, manufacturerTooLong);
            AddTooLong(issues, EquipmentImportColumns.Model, modelTooLong);
            AddTooLong(issues, EquipmentImportColumns.SerialNumber, serialTooLong);
            AddTooLong(issues, EquipmentImportColumns.Notes, notesTooLong);
            AddTooLong(issues, EquipmentImportColumns.Processor, processorTooLong);
            AddTooLong(issues, EquipmentImportColumns.Memory, memoryTooLong);
            AddTooLong(issues, EquipmentImportColumns.Storage, storageTooLong);

            var input = new EquipmentImportRowInput(
                row.RowNumber(), equipmentType, manufacturer, model, serial, serialAmbiguous, notes, processor, memory, storage, formulaFields);

            var fields = EquipmentImportRowValidator.ValidateStructural(input, issues);
            rows.Add((row.RowNumber(), issues, fields));
        }

        // In-file duplicate serial detection (Phase 8 scope).
        var serialGroups = rows.Where(r => r.Fields.NormalizedSerialNumber is not null).GroupBy(r => r.Fields.NormalizedSerialNumber);
        foreach (var group in serialGroups.Where(g => g.Count() > 1))
        {
            foreach (var row in group)
            {
                row.Issues.Add(new ImportIssue(
                    EquipmentImportIssueCodes.DuplicateSerialInFile, EquipmentImportColumns.SerialNumber.Field, DuplicateSerialInFileMessage, ImportIssueSeverity.Error));
            }
        }

        var candidateSerials = rows.Select(r => r.Fields.NormalizedSerialNumber).Where(s => s is not null).Cast<string>().Distinct().ToList();
        var existingSerials = candidateSerials.Count == 0
            ? []
            : await dbContext.Equipment.AsNoTracking()
                .Where(e => e.NormalizedSerialNumber != null && candidateSerials.Contains(e.NormalizedSerialNumber))
                .Select(e => e.NormalizedSerialNumber!).ToListAsync(cancellationToken);
        var existingSerialSet = existingSerials.ToHashSet();

        var candidateTypeNames = rows.Select(r => EquipmentTypeRules.NormalizeName(r.Fields.EquipmentTypeName)).Distinct().ToList();
        var types = candidateTypeNames.Count == 0
            ? []
            : await dbContext.EquipmentTypes.AsNoTracking().Where(t => candidateTypeNames.Contains(t.NormalizedName))
                .Select(t => new { t.Id, t.NormalizedName, t.IsActive, t.Kind }).ToListAsync(cancellationToken);
        var typeByNormalizedName = types.ToDictionary(t => t.NormalizedName, t => t);

        var snapshots = new List<EquipmentImportRowSnapshot>();
        foreach (var (rowNumber, issues, fields) in rows)
        {
            Guid? equipmentTypeId = null;
            if (fields.EquipmentTypeName.Length == 0)
            {
                // Already reported by ValidateStructural (MissingEquipmentType).
            }
            else
            {
                var normalized = EquipmentTypeRules.NormalizeName(fields.EquipmentTypeName);
                if (!typeByNormalizedName.TryGetValue(normalized, out var type))
                {
                    issues.Add(new ImportIssue(EquipmentImportIssueCodes.UnknownEquipmentType, EquipmentImportColumns.EquipmentType.Field, UnknownEquipmentTypeMessage, ImportIssueSeverity.Error));
                }
                else if (!type.IsActive)
                {
                    issues.Add(new ImportIssue(EquipmentImportIssueCodes.InactiveEquipmentType, EquipmentImportColumns.EquipmentType.Field, InactiveEquipmentTypeMessage, ImportIssueSeverity.Error));
                }
                else
                {
                    equipmentTypeId = type.Id;
                    EquipmentImportRowValidator.ValidateAgainstKind(type.Kind, fields, issues);
                }
            }

            if (fields.NormalizedSerialNumber is not null && existingSerialSet.Contains(fields.NormalizedSerialNumber))
            {
                issues.Add(new ImportIssue(EquipmentImportIssueCodes.DuplicateSerialExisting, EquipmentImportColumns.SerialNumber.Field, DuplicateSerialExistingMessage, ImportIssueSeverity.Error));
            }

            var status = ImportRowStatusCalculator.Compute(issues);
            snapshots.Add(new EquipmentImportRowSnapshot(
                rowNumber, status, fields.EquipmentTypeName, equipmentTypeId, fields.Manufacturer, fields.Model,
                fields.SerialNumber, fields.Notes, fields.Processor, fields.Memory, fields.Storage, issues));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var session = new Domain.DataImports.ImportSession
        {
            Id = Guid.NewGuid(),
            ImportType = ImportType.Equipment,
            Status = ImportSessionStatus.Pending,
            OriginalFileName = fileName,
            FileSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(fileBytes)),
            TotalRows = snapshots.Count,
            ValidRows = snapshots.Count(s => s.Status == ImportRowStatus.Valid),
            WarningRows = snapshots.Count(s => s.Status == ImportRowStatus.Warning),
            ErrorRows = snapshots.Count(s => s.Status == ImportRowStatus.Error),
            RowsJson = JsonSerializer.Serialize(snapshots, JsonOptions),
            CreatedByUserId = actorUserId,
            CreatedByDisplayNameSnapshot = actorDisplayName,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(excelReader.Limits.SessionValidityHours),
        };

        dbContext.ImportSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult.Ok(ToPreviewRecord(session, snapshots));
    }

    public async Task<EquipmentImportPreviewRecord?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.ImportSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.ImportType == ImportType.Equipment, cancellationToken);
        if (session is null)
        {
            return null;
        }

        await ClearIfExpiredPendingAsync(session, cancellationToken);
        return ToPreviewRecord(session, DeserializeRows(session.RowsJson));
    }

    /// <summary>Cancels a still-Pending session, clearing its PII snapshot immediately (Phase 8 retention review).</summary>
    public async Task<ServiceResult<EquipmentImportPreviewRecord>> CancelAsync(
        Guid sessionId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.ImportSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.ImportType == ImportType.Equipment, cancellationToken);
        if (session is null)
        {
            return ServiceResult.Fail<EquipmentImportPreviewRecord>(ServiceErrorCode.NotFound, ImportSessionMessages.NotFound);
        }

        if (!ImportSessionRules.CanCancel(session.Status))
        {
            return ServiceResult.Fail<EquipmentImportPreviewRecord>(
                ServiceErrorCode.ImportSessionNotConfirmable, ImportSessionMessages.NotCancellable);
        }

        session.Status = ImportSessionStatus.Cancelled;
        session.RowsJson = null;

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new Auditing.AuditEntry(
            Domain.Auditing.AuditActions.EquipmentImport.Cancelled,
            EntityType: "ImportSession",
            EntityId: session.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { ImportSessionId = session.Id, session.FileSha256 }), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToPreviewRecord(session, []));
    }

    public async Task<(ServiceResult<EquipmentImportConfirmationSummary> Result, IReadOnlyList<EquipmentImportRowSnapshot>? ConflictRows)> ConfirmAsync(
        Guid sessionId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.ImportSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.ImportType == ImportType.Equipment, cancellationToken);
        if (session is null)
        {
            return (ServiceResult.Fail<EquipmentImportConfirmationSummary>(ServiceErrorCode.NotFound, ImportSessionMessages.NotFound), null);
        }

        if (session.ErrorRows > 0)
        {
            return (ServiceResult.Fail<EquipmentImportConfirmationSummary>(
                ServiceErrorCode.ImportPreviewHasErrors, ImportSessionMessages.HasErrors), null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (ImportSessionRules.IsExpiredPending(session.Status, session.ExpiresAtUtc, now))
        {
            await ClearIfExpiredPendingAsync(session, cancellationToken);
            return (ServiceResult.Fail<EquipmentImportConfirmationSummary>(
                ServiceErrorCode.ImportSessionExpired, ImportSessionMessages.Expired), null);
        }

        if (!ImportSessionRules.CanConfirm(session.Status, session.ErrorRows, session.ExpiresAtUtc, now))
        {
            return (ServiceResult.Fail<EquipmentImportConfirmationSummary>(
                ServiceErrorCode.ImportSessionNotConfirmable, ImportSessionMessages.NotConfirmable), null);
        }

        var snapshots = DeserializeRows(session.RowsJson);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var candidateSerials = snapshots.Where(s => s.SerialNumber is not null)
            .Select(s => EquipmentRules.NormalizeSerialNumber(s.SerialNumber)!).Distinct().ToList();
        var existingSerials = candidateSerials.Count == 0
            ? []
            : (await dbContext.Equipment.AsNoTracking()
                .Where(e => e.NormalizedSerialNumber != null && candidateSerials.Contains(e.NormalizedSerialNumber))
                .Select(e => e.NormalizedSerialNumber!).ToListAsync(cancellationToken)).ToHashSet();

        var candidateTypeNames = snapshots.Select(s => EquipmentTypeRules.NormalizeName(s.EquipmentTypeName)).Distinct().ToList();
        var types = candidateTypeNames.Count == 0
            ? []
            : await dbContext.EquipmentTypes.AsNoTracking().Where(t => candidateTypeNames.Contains(t.NormalizedName))
                .Select(t => new { t.Id, t.NormalizedName, t.IsActive, t.Kind }).ToListAsync(cancellationToken);
        var typeByNormalizedName = types.ToDictionary(t => t.NormalizedName, t => t);

        var conflictRows = new List<EquipmentImportRowSnapshot>();
        var revalidated = new List<(EquipmentImportRowSnapshot Snapshot, Guid EquipmentTypeId)>();
        foreach (var snapshot in snapshots)
        {
            var issues = new List<ImportIssue>();
            var normalizedType = EquipmentTypeRules.NormalizeName(snapshot.EquipmentTypeName);
            Guid? equipmentTypeId = null;

            if (!typeByNormalizedName.TryGetValue(normalizedType, out var type))
            {
                issues.Add(new ImportIssue(EquipmentImportIssueCodes.UnknownEquipmentType, EquipmentImportColumns.EquipmentType.Field, UnknownEquipmentTypeMessage, ImportIssueSeverity.Error));
            }
            else if (!type.IsActive)
            {
                issues.Add(new ImportIssue(EquipmentImportIssueCodes.InactiveEquipmentType, EquipmentImportColumns.EquipmentType.Field, InactiveEquipmentTypeMessage, ImportIssueSeverity.Error));
            }
            else if (!EquipmentImportRowValidator.IsImportable(type.Kind))
            {
                // Same rule as the preview — this only catches a session
                // previewed before that rule existed, which would otherwise
                // fail inside EquipmentService.CreateAsync as a "conflict".
                issues.Add(EquipmentImportRowValidator.UnsupportedKind(snapshot.EquipmentTypeName));
            }
            else
            {
                equipmentTypeId = type.Id;
            }

            var normalizedSerial = EquipmentRules.NormalizeSerialNumber(snapshot.SerialNumber);
            if (normalizedSerial is not null && existingSerials.Contains(normalizedSerial))
            {
                issues.Add(new ImportIssue(EquipmentImportIssueCodes.DuplicateSerialExisting, EquipmentImportColumns.SerialNumber.Field, DuplicateSerialExistingMessage, ImportIssueSeverity.Error));
            }

            if (issues.Count > 0)
            {
                conflictRows.Add(snapshot with { Status = ImportRowStatus.Error, Issues = issues });
            }
            else
            {
                revalidated.Add((snapshot, equipmentTypeId!.Value));
            }
        }

        if (conflictRows.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var fullSet = revalidated.Select(r => r.Snapshot).Concat(conflictRows).OrderBy(s => s.RowNumber).ToList();
            return (ServiceResult.Fail<EquipmentImportConfirmationSummary>(
                ServiceErrorCode.ImportPreviewStale, ImportSessionMessages.Stale), fullSet);
        }

        try
        {
            foreach (var (snapshot, equipmentTypeId) in revalidated)
            {
                var laptopSpec = snapshot.Processor is null && snapshot.Memory is null && snapshot.Storage is null
                    ? null
                    : new LaptopSpecificationRecord(snapshot.Processor ?? "", snapshot.Memory ?? "", snapshot.Storage ?? "");

                var createResult = await equipmentService.CreateAsync(
                    new CreateEquipmentData(equipmentTypeId, snapshot.Manufacturer, snapshot.Model, snapshot.SerialNumber, snapshot.Notes, laptopSpec),
                    photos: [], actorUserId, actorDisplayName, skipConditionPhotoRequirement: true, cancellationToken);

                if (!createResult.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (ServiceResult.Fail<EquipmentImportConfirmationSummary>(
                        ServiceErrorCode.ImportPreviewStale, ImportSessionMessages.Conflict), null);
                }
            }

            session.Status = ImportSessionStatus.Completed;
            session.ConfirmedByUserId = actorUserId;
            session.ConfirmedByDisplayNameSnapshot = actorDisplayName;
            session.ConfirmedAtUtc = now;

            // The detailed PII snapshot is no longer needed for any normal
            // operation once the import has actually landed in the real
            // Equipment table — cleared in the same commit as completion
            // (Phase 8 retention review).
            session.RowsJson = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new Auditing.AuditEntry(
                Domain.Auditing.AuditActions.EquipmentImport.Completed,
                EntityType: "ImportSession",
                EntityId: session.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new
                {
                    ImportSessionId = session.Id,
                    RowCount = revalidated.Count,
                    CreatedEquipmentCount = revalidated.Count,
                    session.FileSha256,
                }), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            return (ServiceResult.Fail<EquipmentImportConfirmationSummary>(
                ServiceErrorCode.ImportPreviewStale, ImportSessionMessages.Conflict), null);
        }

        return (ServiceResult.Ok(new EquipmentImportConfirmationSummary(session.Id, session.Status, revalidated.Count, now)), null);
    }

    /// <summary>Clears the PII snapshot in place if — and only if — the session is still Pending and past its expiry; a no-op otherwise (including if it was already cleared).</summary>
    private async Task ClearIfExpiredPendingAsync(Domain.DataImports.ImportSession session, CancellationToken cancellationToken)
    {
        if (session.RowsJson is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (ImportSessionRules.IsExpiredPending(session.Status, session.ExpiresAtUtc, now))
        {
            session.RowsJson = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Empty list once <see cref="Domain.DataImports.ImportSession.RowsJson"/> has been cleared — never a crash on a legitimately-null snapshot.</summary>
    private static List<EquipmentImportRowSnapshot> DeserializeRows(string? rowsJson) =>
        rowsJson is null ? [] : JsonSerializer.Deserialize<List<EquipmentImportRowSnapshot>>(rowsJson, JsonOptions)!;

    private static void AddTooLong(List<ImportIssue> issues, ImportColumn column, bool tooLong)
    {
        if (tooLong)
        {
            issues.Add(new ImportIssue(EquipmentImportIssueCodes.CellTooLong, column.Field, ImportMessages.TooLong(column.Header), ImportIssueSeverity.Error));
        }
    }

    private static EquipmentImportPreviewRecord ToPreviewRecord(Domain.DataImports.ImportSession session, IReadOnlyList<EquipmentImportRowSnapshot> rows) => new(
        session.Id, session.OriginalFileName, session.FileSha256, session.Status,
        session.TotalRows, session.ValidRows, session.WarningRows, session.ErrorRows, session.CreatedAtUtc, rows);
}
