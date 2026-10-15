using System.Data;
using System.Text.Json;
using ClosedXML.Excel;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Companies;
using EquipmentManagement.Domain.Consultants;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Consultant Excel import: template generation, preview (parse + validate,
/// never persists), and confirmation (Phase 8 scope). Reuses
/// <see cref="ConsultantRules"/>/<see cref="AllocationRules"/> (via
/// <see cref="ConsultantImportRowValidator"/>) and, at confirmation,
/// <c>ConsultantService.CreateAsync</c> itself — the same rules and the same
/// creation code path as a normal API call, never a second copy (Phase 8 scope). The workbook and every message are pt-PT; columns are
/// read by their internal field key, whichever accepted header the file uses
/// (ADR-070).
/// </summary>
public sealed class ConsultantImportService(
    ApplicationDbContext dbContext,
    ExcelWorkbookReader excelReader,
    AuditService auditService,
    TimeProvider timeProvider)
{
    private static readonly IReadOnlyList<ImportColumn> Columns = ConsultantImportColumns.All;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string DuplicateNifInFileMessage =
        $"O valor indicado em \"{ConsultantImportColumns.Nif.Header}\" aparece mais do que uma vez no ficheiro.";

    private static readonly string DuplicateNifExistingMessage =
        $"Já existe um consultor com o {ConsultantImportColumns.Nif.Header} indicado.";

    private static readonly string UnknownCompanyMessage =
        $"Não foi possível encontrar a empresa indicada em \"{ConsultantImportColumns.Company.Header}\".";

    private static readonly string InactiveCompanyMessage =
        $"A empresa indicada em \"{ConsultantImportColumns.Company.Header}\" está inativa.";

    /// <summary>
    /// The pt-PT template (ADR-070): an empty, ready-to-fill "Consultores"
    /// sheet plus an "Instruções" sheet. The example lives on the
    /// instructions sheet, never as a data row that would be imported if
    /// left in place.
    /// </summary>
    public static byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Consultores");

        // The strong header marks the five fields every row needs; the
        // allocation block is optional as a whole (see Instruções).
        ExcelSheetLayout.WriteHeaderRow(sheet, [.. Columns.Select(c => c.Header)], i => Columns[i].IsHeaderRequired);
        ExcelSheetLayout.SetColumnWidths(sheet, [32, 16, 34, 20, 44, 14, 24, 28, 32, 16, 16]);

        // Text-format the identifier columns so Excel never silently coerces
        // them to Number, and show dates the pt-PT way (Phase 8 scope).
        sheet.Column(IndexOf(ConsultantImportColumns.Nif)).Style.NumberFormat.Format = ExcelSheetLayout.TextFormat;
        sheet.Column(IndexOf(ConsultantImportColumns.Phone)).Style.NumberFormat.Format = ExcelSheetLayout.TextFormat;
        sheet.Column(IndexOf(ConsultantImportColumns.AllocationStartDate)).Style.DateFormat.Format = ExcelSheetLayout.DateFormat;
        sheet.Column(IndexOf(ConsultantImportColumns.AllocationEndDate)).Style.DateFormat.Format = ExcelSheetLayout.DateFormat;

        // The header row is not a data row: the format above must not make it a date/number cell.
        sheet.Row(1).Style.NumberFormat.Format = ExcelSheetLayout.TextFormat;

        ExcelSheetLayout.AddDropdown(sheet, IndexOf(ConsultantImportColumns.WorkforceType), ExcelDisplayValues.WorkforceTypeLabels);
        ExcelSheetLayout.AddDropdown(sheet, IndexOf(ConsultantImportColumns.EngagementType), ExcelDisplayValues.EngagementTypeLabels);

        string withAllocation = "Se indicar uma alocação";
        string dateGuidance = $"Data no formato {ImportMessages.DateFormatHint}.";
        ExcelSheetLayout.AddInstructionsSheet(
            workbook,
            "Importação de consultores",
            [
                "Preencha a folha \"Consultores\" a partir da linha 2: uma linha por consultor. Não altere o nome das colunas.",
                "Cada linha cria um novo consultor. Um NIF já existente na aplicação é assinalado como erro: a importação nunca atualiza consultores existentes.",
                "As colunas Regime, Tipo de vínculo, Função, Empresa / Cliente, Data de início e Data de fim descrevem a alocação inicial e são opcionais: deixe-as todas vazias para importar apenas o consultor.",
                "Nada é gravado antes de confirmar a importação na aplicação. Se alguma linha tiver erros, nenhuma linha é importada.",
                "No cabeçalho da folha \"Consultores\", a cor mais escura assinala os campos que são sempre obrigatórios.",
            ],
            "Colunas da folha \"Consultores\"",
            [
                new(ConsultantImportColumns.FullName.Header, "Sim", "Nome completo do consultor.", "Maria Exemplo"),
                new(ConsultantImportColumns.Nif.Header, "Sim", "Apenas algarismos. Mantenha a coluna formatada como Texto, para preservar os zeros à esquerda.", "123456789"),
                new(ConsultantImportColumns.Email.Header, "Sim", "Endereço de email do consultor.", "maria.exemplo@example.invalid"),
                new(ConsultantImportColumns.Phone.Header, "Sim", "Mantenha a coluna formatada como Texto.", "+351900000000"),
                new(ConsultantImportColumns.Address.Header, "Sim", "Morada completa.", "Rua Exemplo 123, 1000-001 Lisboa"),
                new(ConsultantImportColumns.WorkforceType.Header, withAllocation, $"Escolha na lista: {string.Join(" ou ", ExcelDisplayValues.WorkforceTypeLabels)}.", ExcelDisplayValues.Label(WorkforceType.External)),
                new(ConsultantImportColumns.EngagementType.Header, withAllocation, $"Escolha na lista: {string.Join(" ou ", ExcelDisplayValues.EngagementTypeLabels)}.", ExcelDisplayValues.Label(EngagementType.ServiceProvider)),
                new(ConsultantImportColumns.RoleOrFunction.Header, withAllocation, "Função desempenhada pelo consultor.", "Consultora de sistemas"),
                new(ConsultantImportColumns.Company.Header, $"Se o regime for {ExcelDisplayValues.Label(WorkforceType.External)}", $"Nome de uma empresa já existente e ativa na aplicação (as empresas não são criadas pela importação). Deixe vazio se o regime for {ExcelDisplayValues.Label(WorkforceType.Internal)}.", "Cliente Exemplo, Lda."),
                new(ConsultantImportColumns.AllocationStartDate.Header, withAllocation, dateGuidance, "15/01/2026"),
                new(ConsultantImportColumns.AllocationEndDate.Header, "Não", $"{dateGuidance} Deixe vazio se a alocação não tiver fim previsto.", string.Empty),
            ]);

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    /// <summary>1-based worksheet column of an import column in a generated template.</summary>
    private static int IndexOf(ImportColumn column) => Columns.ToList().IndexOf(column) + 1;

    public async Task<ServiceResult<ConsultantImportPreviewRecord>> PreviewAsync(
        byte[] fileBytes, string fileName, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var opened = excelReader.OpenAndValidate(fileBytes, fileName);
        if (!opened.Succeeded)
        {
            return ServiceResult.Fail<ConsultantImportPreviewRecord>(opened.ErrorCode, opened.ErrorMessage!);
        }

        using var workbook = opened.Value.Workbook;
        var worksheet = opened.Value.Worksheet;

        var headerMap = ExcelWorkbookReader.MapHeaders(worksheet, Columns);
        if (!headerMap.Succeeded)
        {
            return ServiceResult.Fail<ConsultantImportPreviewRecord>(headerMap.ErrorCode, headerMap.ErrorMessage!);
        }

        var columns = headerMap.Value!;
        int? Col(ImportColumn column) => columns.TryGetValue(column.Field, out var idx) ? idx : null;

        var rows = new List<(int RowNumber, List<ImportIssue> Issues, ConsultantImportRowFields Fields)>();
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

            var fullName = excelReader.ReadText(row, Col(ConsultantImportColumns.FullName), out var fullNameTooLong);
            var nif = excelReader.ReadIdentifierText(row, Col(ConsultantImportColumns.Nif), out var nifAmbiguous, out var nifTooLong);
            var email = excelReader.ReadText(row, Col(ConsultantImportColumns.Email), out var emailTooLong);
            var phone = excelReader.ReadIdentifierText(row, Col(ConsultantImportColumns.Phone), out var phoneAmbiguous, out var phoneTooLong);
            var address = excelReader.ReadText(row, Col(ConsultantImportColumns.Address), out var addressTooLong);
            var workforceType = excelReader.ReadText(row, Col(ConsultantImportColumns.WorkforceType), out _);
            var engagementType = excelReader.ReadText(row, Col(ConsultantImportColumns.EngagementType), out _);
            var roleOrFunction = excelReader.ReadText(row, Col(ConsultantImportColumns.RoleOrFunction), out var roleTooLong);
            var company = excelReader.ReadText(row, Col(ConsultantImportColumns.Company), out var companyTooLong);
            var startDate = ExcelWorkbookReader.ReadDate(row, Col(ConsultantImportColumns.AllocationStartDate), out var startInvalid);
            var endDate = ExcelWorkbookReader.ReadDate(row, Col(ConsultantImportColumns.AllocationEndDate), out var endInvalid);

            var issues = new List<ImportIssue>();
            AddTooLong(issues, ConsultantImportColumns.FullName, fullNameTooLong);
            AddTooLong(issues, ConsultantImportColumns.Nif, nifTooLong);
            AddTooLong(issues, ConsultantImportColumns.Email, emailTooLong);
            AddTooLong(issues, ConsultantImportColumns.Phone, phoneTooLong);
            AddTooLong(issues, ConsultantImportColumns.Address, addressTooLong);
            AddTooLong(issues, ConsultantImportColumns.RoleOrFunction, roleTooLong);
            AddTooLong(issues, ConsultantImportColumns.Company, companyTooLong);

            var input = new ConsultantImportRowInput(
                row.RowNumber(), fullName, nif, nifAmbiguous, email, address, phone, phoneAmbiguous,
                workforceType, engagementType, roleOrFunction, company, startDate, startInvalid, endDate, endInvalid, formulaFields);

            var fields = ConsultantImportRowValidator.ValidateStructural(input, issues);
            rows.Add((row.RowNumber(), issues, fields));
        }

        // In-file duplicate NIF detection (Phase 8 scope).
        var nifGroups = rows.Where(r => r.Fields.Nif.Length > 0).GroupBy(r => r.Fields.Nif);
        foreach (var group in nifGroups.Where(g => g.Count() > 1))
        {
            foreach (var row in group)
            {
                row.Issues.Add(new ImportIssue(
                    ConsultantImportIssueCodes.DuplicateNifInFile, ConsultantImportColumns.Nif.Field, DuplicateNifInFileMessage, ImportIssueSeverity.Error));
            }
        }

        // Database duplicate NIF detection (batch).
        var candidateNifs = rows.Select(r => r.Fields.Nif).Where(n => n.Length > 0).Distinct().ToList();
        var existingNifs = candidateNifs.Count == 0
            ? []
            : await dbContext.Consultants.AsNoTracking().Where(c => candidateNifs.Contains(c.Nif)).Select(c => c.Nif).ToListAsync(cancellationToken);
        var existingNifSet = existingNifs.ToHashSet();

        // Company lookup (batch) for allocation-bearing rows.
        var candidateCompanyNames = rows.Where(r => r.Fields.CompanyName is not null)
            .Select(r => CompanyRules.NormalizeName(r.Fields.CompanyName!)).Distinct().ToList();
        var companies = candidateCompanyNames.Count == 0
            ? []
            : await dbContext.Companies.AsNoTracking().Where(c => candidateCompanyNames.Contains(c.NormalizedName))
                .Select(c => new { c.Id, c.NormalizedName, c.IsActive }).ToListAsync(cancellationToken);
        var companyByNormalizedName = companies.ToDictionary(c => c.NormalizedName, c => c);

        var snapshots = new List<ConsultantImportRowSnapshot>();
        foreach (var (rowNumber, issues, fields) in rows)
        {
            if (fields.Nif.Length > 0 && existingNifSet.Contains(fields.Nif))
            {
                issues.Add(new ImportIssue(
                    ConsultantImportIssueCodes.DuplicateNifExisting, ConsultantImportColumns.Nif.Field, DuplicateNifExistingMessage, ImportIssueSeverity.Error));
            }

            Guid? companyId = null;
            if (fields.CompanyName is not null)
            {
                var normalized = CompanyRules.NormalizeName(fields.CompanyName);
                if (!companyByNormalizedName.TryGetValue(normalized, out var company))
                {
                    issues.Add(new ImportIssue(ConsultantImportIssueCodes.UnknownCompany, ConsultantImportColumns.Company.Field, UnknownCompanyMessage, ImportIssueSeverity.Error));
                }
                else if (!company.IsActive)
                {
                    issues.Add(new ImportIssue(ConsultantImportIssueCodes.InactiveCompany, ConsultantImportColumns.Company.Field, InactiveCompanyMessage, ImportIssueSeverity.Error));
                }
                else
                {
                    companyId = company.Id;
                }
            }

            var status = ImportRowStatusCalculator.Compute(issues);
            snapshots.Add(new ConsultantImportRowSnapshot(
                rowNumber, status, fields.FullName, fields.Nif, fields.Email, fields.Address, fields.Phone,
                fields.HasAllocation, fields.WorkforceType, fields.EngagementType, fields.RoleOrFunction, fields.CompanyName,
                companyId, fields.AllocationStartDate, fields.AllocationEndDate, issues));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var session = new ImportSession
        {
            Id = Guid.NewGuid(),
            ImportType = ImportType.Consultants,
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

    /// <summary>
    /// Reading a session is also an "access" point for lazy PII cleanup
    /// (Phase 8 retention review): a still-Pending session found
    /// past its expiry here has its <see cref="ImportSession.RowsJson"/>
    /// cleared on the spot, since it can never be confirmed again regardless
    /// (see <see cref="ImportSessionRules.IsExpiredPending"/>). No background
    /// job is introduced for this — cleanup only ever happens as a side
    /// effect of a real request touching that exact row.
    /// </summary>
    public async Task<ConsultantImportPreviewRecord?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.ImportSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.ImportType == ImportType.Consultants, cancellationToken);
        if (session is null)
        {
            return null;
        }

        await ClearIfExpiredPendingAsync(session, cancellationToken);

        return ToPreviewRecord(session, DeserializeRows(session.RowsJson));
    }

    /// <summary>
    /// Explicitly discards a Pending session before its natural expiry
    /// (Phase 8 scope; Phase 8 retention review) — the
    /// deliberate, immediate version of the same PII cleanup that would
    /// otherwise happen lazily once the session expires unconfirmed.
    /// </summary>
    public async Task<ServiceResult<ConsultantImportPreviewRecord>> CancelAsync(
        Guid sessionId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.ImportSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.ImportType == ImportType.Consultants, cancellationToken);
        if (session is null)
        {
            return ServiceResult.Fail<ConsultantImportPreviewRecord>(ServiceErrorCode.NotFound, ImportSessionMessages.NotFound);
        }

        if (!ImportSessionRules.CanCancel(session.Status))
        {
            return ServiceResult.Fail<ConsultantImportPreviewRecord>(
                ServiceErrorCode.ImportSessionNotConfirmable, ImportSessionMessages.NotCancellable);
        }

        session.Status = ImportSessionStatus.Cancelled;
        session.RowsJson = null;

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.ConsultantImport.Cancelled,
            EntityType: "ImportSession",
            EntityId: session.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { ImportSessionId = session.Id, session.FileSha256 }), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToPreviewRecord(session, []));
    }

    /// <summary>
    /// Confirms the session (Phase 8 scope). Returns a
    /// successful summary, or — on a stale-preview conflict — a null
    /// <c>Summary</c> with <c>ConflictRows</c> populated with freshly
    /// re-validated rows; the stored session is left untouched on conflict
    /// so it remains retriable once the underlying database state is fixed.
    /// </summary>
    public async Task<(ServiceResult<ConsultantImportConfirmationSummary> Result, IReadOnlyList<ConsultantImportRowSnapshot>? ConflictRows)> ConfirmAsync(
        Guid sessionId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.ImportSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.ImportType == ImportType.Consultants, cancellationToken);
        if (session is null)
        {
            return (ServiceResult.Fail<ConsultantImportConfirmationSummary>(ServiceErrorCode.NotFound, ImportSessionMessages.NotFound), null);
        }

        if (session.ErrorRows > 0)
        {
            return (ServiceResult.Fail<ConsultantImportConfirmationSummary>(
                ServiceErrorCode.ImportPreviewHasErrors, ImportSessionMessages.HasErrors), null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (ImportSessionRules.IsExpiredPending(session.Status, session.ExpiresAtUtc, now))
        {
            // Distinct from "not confirmable" (Completed/Cancelled): this is
            // specifically the time-based case, and — since it can never be
            // confirmed from here regardless — the moment to lazily discard
            // the now-pointless PII snapshot (Phase 8 retention review).
            await ClearIfExpiredPendingAsync(session, cancellationToken);
            return (ServiceResult.Fail<ConsultantImportConfirmationSummary>(
                ServiceErrorCode.ImportSessionExpired, ImportSessionMessages.Expired), null);
        }

        if (!ImportSessionRules.CanConfirm(session.Status, session.ErrorRows, session.ExpiresAtUtc, now))
        {
            return (ServiceResult.Fail<ConsultantImportConfirmationSummary>(
                ServiceErrorCode.ImportSessionNotConfirmable, ImportSessionMessages.NotConfirmable), null);
        }

        var snapshots = DeserializeRows(session.RowsJson);

        // Read Committed (the default) is deliberate, not Serializable — see ADR-057: NIF's unique index is
        // the real backstop, and the allocation-overlap check below is
        // structurally race-free because it targets a Consultant row created
        // inside this same, still-uncommitted transaction.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Re-validate every DB-sensitive fact fresh (Phase 8 scope).
        var candidateNifs = snapshots.Select(s => s.Nif).Distinct().ToList();
        var existingNifs = (await dbContext.Consultants.AsNoTracking()
            .Where(c => candidateNifs.Contains(c.Nif)).Select(c => c.Nif).ToListAsync(cancellationToken)).ToHashSet();

        var candidateCompanyNames = snapshots.Where(s => s.CompanyName is not null)
            .Select(s => CompanyRules.NormalizeName(s.CompanyName!)).Distinct().ToList();
        var companies = candidateCompanyNames.Count == 0
            ? []
            : await dbContext.Companies.AsNoTracking().Where(c => candidateCompanyNames.Contains(c.NormalizedName))
                .Select(c => new { c.Id, c.NormalizedName, c.IsActive }).ToListAsync(cancellationToken);
        var companyByNormalizedName = companies.ToDictionary(c => c.NormalizedName, c => c);

        var conflictRows = new List<ConsultantImportRowSnapshot>();
        var revalidated = new List<(ConsultantImportRowSnapshot Snapshot, Guid? CompanyId)>();
        foreach (var snapshot in snapshots)
        {
            var issues = new List<ImportIssue>();
            if (existingNifs.Contains(snapshot.Nif))
            {
                issues.Add(new ImportIssue(ConsultantImportIssueCodes.DuplicateNifExisting, ConsultantImportColumns.Nif.Field, DuplicateNifExistingMessage, ImportIssueSeverity.Error));
            }

            Guid? companyId = null;
            if (snapshot.CompanyName is not null)
            {
                var normalized = CompanyRules.NormalizeName(snapshot.CompanyName);
                if (!companyByNormalizedName.TryGetValue(normalized, out var company))
                {
                    issues.Add(new ImportIssue(ConsultantImportIssueCodes.UnknownCompany, ConsultantImportColumns.Company.Field, UnknownCompanyMessage, ImportIssueSeverity.Error));
                }
                else if (!company.IsActive)
                {
                    issues.Add(new ImportIssue(ConsultantImportIssueCodes.InactiveCompany, ConsultantImportColumns.Company.Field, InactiveCompanyMessage, ImportIssueSeverity.Error));
                }
                else
                {
                    companyId = company.Id;
                }
            }

            if (issues.Count > 0)
            {
                conflictRows.Add(snapshot with { Status = ImportRowStatus.Error, Issues = issues });
            }
            else
            {
                revalidated.Add((snapshot, companyId));
            }
        }

        if (conflictRows.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            // Full row set returned (conflicting rows carry the fresh issues;
            // the rest are unchanged) so the caller can show a complete,
            // accurate picture without a second round-trip.
            var fullSet = revalidated.Select(r => r.Snapshot).Concat(conflictRows).OrderBy(s => s.RowNumber).ToList();
            return (ServiceResult.Fail<ConsultantImportConfirmationSummary>(
                ServiceErrorCode.ImportPreviewStale, ImportSessionMessages.Stale), fullSet);
        }

        var createdAllocations = 0;
        try
        {
            foreach (var (snapshot, companyId) in revalidated)
            {
                var createResult = await CreateConsultantAsync(snapshot, actorUserId, actorDisplayName, cancellationToken);
                if (!createResult.Succeeded)
                {
                    // A DB-level race the pre-checks above didn't catch (two
                    // concurrent imports of the same NIF) — the unique index
                    // is the final backstop (Phase 8 scope).
                    await transaction.RollbackAsync(cancellationToken);
                    return (ServiceResult.Fail<ConsultantImportConfirmationSummary>(
                        ServiceErrorCode.ImportPreviewStale, ImportSessionMessages.Conflict), null);
                }

                if (snapshot.HasAllocation)
                {
                    CreateInitialAllocation(createResult.Value!.Id, snapshot, companyId, now);
                    createdAllocations++;
                }
            }

            session.Status = ImportSessionStatus.Completed;
            session.ConfirmedByUserId = actorUserId;
            session.ConfirmedByDisplayNameSnapshot = actorDisplayName;
            session.ConfirmedAtUtc = now;
            // The detailed PII snapshot is no longer needed for any normal
            // operation once the import has actually landed in the real
            // Consultant/ConsultantAllocation tables — cleared in the same
            // commit as completion (Phase 8 retention review).
            session.RowsJson = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ConsultantImport.Completed,
                EntityType: "ImportSession",
                EntityId: session.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new
                {
                    ImportSessionId = session.Id,
                    RowCount = revalidated.Count,
                    CreatedConsultantCount = revalidated.Count,
                    CreatedAllocationCount = createdAllocations,
                    session.FileSha256,
                }), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            return (ServiceResult.Fail<ConsultantImportConfirmationSummary>(
                ServiceErrorCode.ImportPreviewStale, ImportSessionMessages.Conflict), null);
        }

        return (ServiceResult.Ok(new ConsultantImportConfirmationSummary(session.Id, session.Status, revalidated.Count, createdAllocations, now)), null);
    }

    /// <summary>
    /// Same validation/creation as <c>ConsultantService.CreateAsync</c>,
    /// inlined here rather than calling it, so it can safely participate in
    /// the batch confirmation's single ambient transaction (see ADR-057 for why the full service method
    /// cannot be reused directly for the allocation half of this operation).
    /// </summary>
    private async Task<ServiceResult<Consultant>> CreateConsultantAsync(
        ConsultantImportRowSnapshot snapshot, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var consultant = new Consultant
        {
            Id = Guid.NewGuid(),
            FullName = snapshot.FullName,
            Nif = snapshot.Nif,
            Email = snapshot.Email,
            Address = snapshot.Address,
            Phone = snapshot.Phone,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        dbContext.Consultants.Add(consultant);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            return ServiceResult.Fail<Consultant>(ServiceErrorCode.DuplicateNif, "A consultant with this NIF already exists.");
        }

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.Consultant.Created,
            EntityType: "Consultant",
            EntityId: consultant.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { consultant.FullName }), cancellationToken);

        return ServiceResult.Ok(consultant);
    }

    /// <summary>
    /// The overlap check normal allocation creation performs is unnecessary
    /// here — this Consultant row was created moments earlier inside the
    /// very transaction still open, so no other session can possibly hold a
    /// competing allocation for it yet (Phase 8 scope). No
    /// per-allocation audit entry is written here — the batch summary's
    /// <c>createdAllocationCount</c> covers it (Phase 8 scope).
    /// </summary>
    private void CreateInitialAllocation(Guid consultantId, ConsultantImportRowSnapshot snapshot, Guid? companyId, DateTime now)
    {
        dbContext.ConsultantAllocations.Add(new ConsultantAllocation
        {
            Id = Guid.NewGuid(),
            ConsultantId = consultantId,
            CompanyId = companyId,
            WorkforceType = snapshot.WorkforceType!.Value,
            EngagementType = snapshot.EngagementType!.Value,
            RoleOrFunction = snapshot.RoleOrFunction!,
            StartDate = snapshot.AllocationStartDate!.Value,
            EndDate = snapshot.AllocationEndDate,
            Status = AllocationStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
    }

    /// <summary>Clears the PII snapshot in place if — and only if — the session is still Pending and past its expiry; a no-op otherwise (including if it was already cleared).</summary>
    private async Task ClearIfExpiredPendingAsync(ImportSession session, CancellationToken cancellationToken)
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

    /// <summary>Empty list once <see cref="ImportSession.RowsJson"/> has been cleared — never a crash on a legitimately-null snapshot.</summary>
    private static List<ConsultantImportRowSnapshot> DeserializeRows(string? rowsJson) =>
        rowsJson is null ? [] : JsonSerializer.Deserialize<List<ConsultantImportRowSnapshot>>(rowsJson, JsonOptions)!;

    private static void AddTooLong(List<ImportIssue> issues, ImportColumn column, bool tooLong)
    {
        if (tooLong)
        {
            issues.Add(new ImportIssue(ConsultantImportIssueCodes.CellTooLong, column.Field, ImportMessages.TooLong(column.Header), ImportIssueSeverity.Error));
        }
    }

    private static ConsultantImportPreviewRecord ToPreviewRecord(ImportSession session, IReadOnlyList<ConsultantImportRowSnapshot> rows) => new(
        session.Id, session.OriginalFileName, session.FileSha256, session.Status,
        session.TotalRows, session.ValidRows, session.WarningRows, session.ErrorRows, session.CreatedAtUtc, rows);
}
