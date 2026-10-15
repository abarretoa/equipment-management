using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Companies;
using EquipmentManagement.Domain.Consultants;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.ClientProfiles;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Equipments;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Current Inventory import: one workbook establishes the real, current
/// state — Consultants (Interno = the deployment company's employee, Externo =
/// consultor externo, both the existing <see cref="Consultant"/> model told
/// apart by the allocation's <see cref="WorkforceType"/>), their current
/// <see cref="ConsultantAllocation"/>, the Equipment, and who currently holds
/// it (an Active <see cref="EquipmentAssignment"/> with
/// <see cref="EquipmentAssignmentOrigin.CurrentInventoryImport"/>). One row is
/// one piece of Equipment; the same person may appear on many rows and is
/// created once (identity: NIF).
///
/// This records custody that already exists — it is not a delivery. It never
/// creates a Handover, dispatch, Responsibility Term, Return, photo set or
/// email, and never updates an existing Consultant, Allocation or Equipment.
/// Same Preview → Confirm architecture as the Consultant and Equipment
/// imports (an <see cref="ImportSession"/>, errors block confirmation, the
/// PII snapshot is cleared once the session ends), but its own service: those
/// two flows stay unchanged and independently usable.
/// </summary>
public sealed class CurrentInventoryImportService(
    ApplicationDbContext dbContext,
    ExcelWorkbookReader excelReader,
    EquipmentService equipmentService,
    AuditService auditService,
    TimeProvider timeProvider,
    ClientProfile clientProfile)
{
    public const string TemplateFileName = "Modelo_Importacao_Inventario_Atual.xlsx";

    public const string DataSheetName = "Inventário atual";

    /// <summary>The Excel Table over the data sheet's input area — the workbook's only filtering mechanism.</summary>
    public const string InventoryTableName = "InventarioAtual";

    /// <summary>Very hidden support sheet holding the generated dropdown lists.</summary>
    public const string ListsSheetName = "Listas";

    /// <summary>Defined name the "Tipo de equipamento" cell dropdown reads from.</summary>
    public const string EquipmentTypeListName = "TiposDeEquipamento";

    private static readonly IReadOnlyList<ImportColumn> Columns = CurrentInventoryImportColumns.All;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The fields that describe one person and their current allocation:
    /// every row carrying the same NIF must agree on all of them, and they are
    /// what an existing Consultant/Allocation is compared against. The
    /// Data de atribuição is per equipment, so it is deliberately not here.
    /// </summary>
    private static readonly (ImportColumn Column, Func<CurrentInventoryImportRowFields, string> Key)[] PersonFields =
    [
        (CurrentInventoryImportColumns.FullName, f => TextKey(f.FullName)),
        (CurrentInventoryImportColumns.Email, f => TextKey(f.Email)),
        (CurrentInventoryImportColumns.Phone, f => PhoneKey(f.Phone)),
        (CurrentInventoryImportColumns.Address, f => TextKey(f.Address)),
        (CurrentInventoryImportColumns.WorkforceType, f => f.WorkforceType?.ToString() ?? string.Empty),
        (CurrentInventoryImportColumns.EngagementType, f => f.EngagementType?.ToString() ?? string.Empty),
        (CurrentInventoryImportColumns.RoleOrFunction, f => TextKey(f.RoleOrFunction)),
        (CurrentInventoryImportColumns.Company, f => f.CompanyName is null ? string.Empty : CompanyRules.NormalizeName(f.CompanyName)),
        (CurrentInventoryImportColumns.AllocationStartDate, f => f.AllocationStartDate?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
        (CurrentInventoryImportColumns.AllocationEndDate, f => f.AllocationEndDate?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
    ];

    private sealed record RowWork(int RowNumber, List<ImportIssue> Issues, CurrentInventoryImportRowFields Fields);

    private sealed record PersonPlan(
        CurrentInventoryConsultantAction ConsultantAction, Guid? ExistingConsultantId,
        CurrentInventoryAllocationAction AllocationAction, Guid? ExistingAllocationId);

    // ------------------------------------------------------------------
    // Template
    // ------------------------------------------------------------------

    /// <summary>
    /// The pt-PT template: an empty, ready-to-fill "Inventário atual" sheet
    /// plus an "Instruções" sheet. Examples live only on the instructions
    /// sheet, never in a data row that would be imported if left in place.
    /// The currently active equipment type names are listed in the
    /// instructions (types are database-managed, so they are read when the
    /// template is generated rather than frozen into a dropdown that would go
    /// stale); the type cell stays free text and the backend validates it.
    /// </summary>
    public async Task<byte[]> GenerateTemplateAsync(CancellationToken cancellationToken = default)
    {
        var activeTypes = await dbContext.EquipmentTypes.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new { t.Name, t.Kind })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(DataSheetName);

        // Only "Tipo de equipamento" is needed on every row — the strong
        // header marks exactly that (same convention as the other templates);
        // everything else depends on the type or on the row being assigned.
        ExcelSheetLayout.WriteHeaderRow(sheet, [.. Columns.Select(c => c.Header)], i => Columns[i] == CurrentInventoryImportColumns.EquipmentType);
        ExcelSheetLayout.SetColumnWidths(sheet, [24, 18, 22, 22, 34, 24, 16, 20, 20, 30, 24, 30, 14, 32, 18, 40, 13, 24, 26, 30, 16, 16, 20]);

        // Identifiers as Text, so Excel never turns them into numbers and
        // drops leading zeros or a leading "+"; dates shown the pt-PT way.
        foreach (var column in new[]
                 {
                     CurrentInventoryImportColumns.SerialNumber, CurrentInventoryImportColumns.Imei, CurrentInventoryImportColumns.MobileNumber,
                     CurrentInventoryImportColumns.SimSerialNumber, CurrentInventoryImportColumns.Nif, CurrentInventoryImportColumns.Phone,
                 })
        {
            sheet.Column(IndexOf(column)).Style.NumberFormat.Format = ExcelSheetLayout.TextFormat;
        }

        foreach (var column in new[]
                 {
                     CurrentInventoryImportColumns.AllocationStartDate, CurrentInventoryImportColumns.AllocationEndDate, CurrentInventoryImportColumns.AssignmentDate,
                 })
        {
            sheet.Column(IndexOf(column)).Style.DateFormat.Format = ExcelSheetLayout.DateFormat;
        }

        foreach (var column in new[] { CurrentInventoryImportColumns.Notes, CurrentInventoryImportColumns.Address })
        {
            sheet.Column(IndexOf(column)).Style.Alignment.WrapText = true;
        }

        // The header row is not a data row: the formats above must not make it a date/number/wrapped cell.
        sheet.Row(1).Style.NumberFormat.Format = ExcelSheetLayout.TextFormat;
        sheet.Row(1).Style.Alignment.WrapText = false;

        ExcelSheetLayout.AddDropdown(sheet, IndexOf(CurrentInventoryImportColumns.WorkforceType), ExcelDisplayValues.WorkforceTypeLabels);
        ExcelSheetLayout.AddDropdown(sheet, IndexOf(CurrentInventoryImportColumns.EngagementType), ExcelDisplayValues.EngagementTypeLabels);

        // Filtering through a native Excel Table, never a worksheet AutoFilter:
        // with a worksheet AutoFilter, re-filtering in a later Excel session
        // (after rows were added) leaves Excel's old _FilterDatabase name next
        // to its new one, and the next open stops on "Nomes em conflito". A
        // Table keeps its filter inside the table, uses no such workbook name,
        // and grows as HR types rows below it. It starts with one empty data
        // row (a Table needs one); the import ignores fully blank rows. No
        // theme or banding, so the header keeps the template's own styling.
        var table = sheet.Range(1, 1, 2, Columns.Count).CreateTable(InventoryTableName);
        table.Theme = XLTableTheme.None;
        table.ShowRowStripes = false;
        table.ShowColumnStripes = false;
        table.ShowAutoFilter = true;

        var internalLabel = ExcelDisplayValues.Label(WorkforceType.Internal);
        var organization = clientProfile.Identity.OrganizationName;
        var externalLabel = ExcelDisplayValues.Label(WorkforceType.External);
        // A real cell dropdown for "Tipo de equipamento": the active type names
        // (read now, from the database — never hard-coded) go to a very
        // hidden support sheet, exposed through a defined name, so the list
        // has no 255-character inline-list limit. Only the name is offered;
        // the import still resolves the EquipmentType record and its Kind.
        if (activeTypes.Count > 0)
        {
            var lists = workbook.Worksheets.Add(ListsSheetName);
            for (var i = 0; i < activeTypes.Count; i++)
            {
                lists.Cell(i + 1, 1).SetValue(activeTypes[i].Name);
            }

            workbook.DefinedNames.Add(EquipmentTypeListName, lists.Range(1, 1, activeTypes.Count, 1));
            lists.Visibility = XLWorksheetVisibility.VeryHidden;

            var typeColumn = IndexOf(CurrentInventoryImportColumns.EquipmentType);
            var validation = sheet.Range(2, typeColumn, excelReader.Limits.MaxRowsPerImport + 1, typeColumn).CreateDataValidation();
            validation.List(EquipmentTypeListName, true);
            validation.ErrorStyle = XLErrorStyle.Stop;
            validation.ShowErrorMessage = true;
            validation.ErrorTitle = CurrentInventoryImportColumns.EquipmentType.Header;
            validation.ErrorMessage = "Escolha um tipo de equipamento da lista.";
        }

        var typeList = activeTypes.Count == 0
            ? "Ainda não existe nenhum tipo de equipamento ativo na plataforma: crie ou reative os tipos necessários no menu \"Tipos de equipamento\" e descarregue o modelo novamente."
            : "Na coluna \"Tipo de equipamento\", escolha o tipo na lista da célula: a lista contém os tipos ativos na plataforma quando este modelo foi descarregado. Se for criado um tipo novo, descarregue o modelo novamente.";

        const string assigned = "Se o equipamento estiver atribuído";
        var dateGuidance = $"Data no formato {ImportMessages.DateFormatHint}.";
        ExcelSheetLayout.AddInstructionsSheet(
            workbook,
            "Importação do inventário atual",
            [
                $"Preencha a folha \"{DataSheetName}\" a partir da linha 2. Cada linha corresponde a um equipamento. Não altere o nome nem a ordem das colunas.",
                "A mesma pessoa pode aparecer em várias linhas (por exemplo, um portátil, um monitor e um rato): o sistema identifica-a pelo NIF e cria-a uma única vez. Os dados da pessoa e da alocação têm de ser iguais em todas as linhas com o mesmo NIF.",
                $"Regime = {internalLabel} significa funcionário interno da {organization}; Regime = {externalLabel} significa consultor externo. Os valores aceites para Regime são {internalLabel} e {externalLabel}; para Tipo de vínculo são {string.Join(" e ", ExcelDisplayValues.EngagementTypeLabels)}.",
                "O sistema não deduz o Regime nem o Tipo de vínculo a partir de nenhum outro dado (empresa, email, função ou outro): ambos têm de ser indicados.",
                $"Com Regime = {externalLabel}, indique em \"Empresa / Cliente\" uma empresa já existente e ativa na plataforma. Com Regime = {internalLabel}, deixe \"Empresa / Cliente\" vazio.",
                "Equipamento em stock (sem ninguém com ele): deixe vazias todas as colunas da pessoa, da alocação e a Data de atribuição. O equipamento fica Disponível.",
                "Equipamento atribuído: preencha todos os dados da pessoa e da alocação atual. Se qualquer uma dessas colunas estiver preenchida, a linha é tratada como equipamento atribuído e os restantes dados obrigatórios passam a ser exigidos.",
                "Se a pessoa já existir na plataforma (mesmo NIF), é reutilizada e os seus dados não são alterados. Se a pessoa já tiver uma alocação ativa, os dados de alocação do ficheiro têm de ser iguais aos registados; caso contrário a linha é assinalada como erro.",
                "O Código do equipamento (por exemplo, PT-0034) é gerado automaticamente pela plataforma no momento da confirmação. Não preencha nem invente códigos.",
                "Um Número de série ou IMEI repetido no ficheiro, ou já existente na plataforma, é assinalado como erro: esta importação só cria equipamentos novos e nunca atualiza equipamentos existentes.",
                "A Data de atribuição é a data desde quando a pessoa tem o equipamento (a atribuição atual) e não representa uma nova entrega. Se ficar vazia, é usada a data e hora da confirmação da importação.",
                "A importação não envia emails, não cria Termos de Responsabilidade e não cria entregas, envios ou devoluções fictícias.",
                "Portáteis e telemóveis já existentes podem ser importados sem fotografias. As fotografias do estado do equipamento podem ser adicionadas mais tarde na plataforma.",
                $"Para telemóveis, o IMEI é obrigatório e tem exatamente 15 dígitos (pode separá-los com espaços ou hífenes); o {CurrentInventoryImportColumns.MobileNumber.Header} e o {CurrentInventoryImportColumns.SimSerialNumber.Header} são opcionais.",
                "Nunca indique o PIN, o PUK, palavras-passe ou outros dados confidenciais neste ficheiro.",
                $"Datas no formato {ImportMessages.DateFormatHint}.",
                "Nada é gravado antes de confirmar a importação na plataforma. Se alguma linha tiver erros, nenhuma linha é importada.",
                $"No cabeçalho da folha \"{DataSheetName}\", a cor mais escura assinala o único campo sempre obrigatório (Tipo de equipamento); a obrigatoriedade das restantes colunas depende do tipo de equipamento e de o equipamento estar atribuído (ver tabela abaixo).",
                typeList,
            ],
            $"Colunas da folha \"{DataSheetName}\"",
            [
                new(CurrentInventoryImportColumns.EquipmentType.Header, "Sim", "Escolha na lista da célula um tipo de equipamento ativo na plataforma. Os tipos não são criados pela importação.", "Portátil"),
                new(CurrentInventoryImportColumns.Manufacturer.Header, "Portáteis e telemóveis", "Fabricante do equipamento.", "Dell"),
                new(CurrentInventoryImportColumns.Model.Header, "Não", "Modelo do equipamento.", "Latitude 5440"),
                new(CurrentInventoryImportColumns.SerialNumber.Header, "Portáteis", "Mantenha a coluna formatada como Texto, para preservar os zeros à esquerda.", "0000000EXEMPLO"),
                new(CurrentInventoryImportColumns.Notes.Header, "Não", "Informação adicional sobre o equipamento.", string.Empty),
                new(CurrentInventoryImportColumns.Processor.Header, "Portáteis", "Deixe vazio nos equipamentos que não são portáteis.", "Intel Core i5-1345U"),
                new(CurrentInventoryImportColumns.Memory.Header, "Portáteis", "Deixe vazio nos equipamentos que não são portáteis.", "16 GB"),
                new(CurrentInventoryImportColumns.Storage.Header, "Portáteis", "Deixe vazio nos equipamentos que não são portáteis.", "512 GB SSD"),
                new(CurrentInventoryImportColumns.Imei.Header, "Telemóveis", "Identificador único do telemóvel: exatamente 15 dígitos. Mantenha a coluna formatada como Texto. Deixe vazio nos equipamentos que não são telemóveis.", "350000000000000"),
                new(CurrentInventoryImportColumns.MobileNumber.Header, "Não", "Apenas para telemóveis.", "+351910000000"),
                new(CurrentInventoryImportColumns.SimSerialNumber.Header, "Não", "Apenas para telemóveis.", "8935100000000000000"),
                new(CurrentInventoryImportColumns.FullName.Header, assigned, "Nome completo da pessoa que tem o equipamento.", "Maria Exemplo"),
                new(CurrentInventoryImportColumns.Nif.Header, assigned, "Apenas algarismos. Identifica a pessoa: o mesmo NIF em várias linhas é a mesma pessoa.", "123456789"),
                new(CurrentInventoryImportColumns.Email.Header, assigned, "Endereço de email da pessoa.", "maria.exemplo@example.invalid"),
                new(CurrentInventoryImportColumns.Phone.Header, assigned, "Mantenha a coluna formatada como Texto.", "+351900000000"),
                new(CurrentInventoryImportColumns.Address.Header, assigned, "Morada completa.", "Rua Exemplo 123, 1000-001 Lisboa"),
                new(CurrentInventoryImportColumns.WorkforceType.Header, assigned, $"Escolha na lista: {internalLabel} (funcionário interno da {organization}) ou {externalLabel} (consultor externo).", internalLabel),
                new(CurrentInventoryImportColumns.EngagementType.Header, assigned, $"Escolha na lista: {string.Join(" ou ", ExcelDisplayValues.EngagementTypeLabels)}.", ExcelDisplayValues.Label(EngagementType.EmploymentContract)),
                new(CurrentInventoryImportColumns.RoleOrFunction.Header, assigned, "Função desempenhada pela pessoa.", "Consultora de sistemas"),
                new(CurrentInventoryImportColumns.Company.Header, $"Se o regime for {externalLabel}", $"Nome de uma empresa já existente e ativa na plataforma. Deixe vazio se o regime for {internalLabel}.", "Cliente Exemplo, Lda."),
                new(CurrentInventoryImportColumns.AllocationStartDate.Header, assigned, $"Início da alocação atual. {dateGuidance}", "15/01/2026"),
                new(CurrentInventoryImportColumns.AllocationEndDate.Header, "Não", $"{dateGuidance} Deixe vazio se a alocação não tiver fim previsto.", string.Empty),
                new(CurrentInventoryImportColumns.AssignmentDate.Header, "Não", $"Desde quando a pessoa tem este equipamento. {dateGuidance} Não pode ser uma data futura. Se ficar vazia, é usada a data e hora da confirmação.", "01/02/2026"),
            ]);

        // The hidden support sheet goes last, after the two sheets HR uses;
        // the data sheet stays first (the import reads the first sheet) and active.
        if (workbook.Worksheets.TryGetWorksheet(ListsSheetName, out var listsSheet))
        {
            listsSheet.Position = workbook.Worksheets.Count;
        }

        sheet.SetTabActive();

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    /// <summary>1-based worksheet column of an import column in a generated template.</summary>
    private static int IndexOf(ImportColumn column) => Columns.ToList().IndexOf(column) + 1;

    // ------------------------------------------------------------------
    // Preview
    // ------------------------------------------------------------------

    public async Task<ServiceResult<CurrentInventoryImportPreviewRecord>> PreviewAsync(
        byte[] fileBytes, string fileName, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var opened = excelReader.OpenAndValidate(fileBytes, fileName);
        if (!opened.Succeeded)
        {
            return ServiceResult.Fail<CurrentInventoryImportPreviewRecord>(opened.ErrorCode, opened.ErrorMessage!);
        }

        using var workbook = opened.Value.Workbook;
        var worksheet = opened.Value.Worksheet;

        var headerMap = ExcelWorkbookReader.MapHeaders(worksheet, Columns);
        if (!headerMap.Succeeded)
        {
            return ServiceResult.Fail<CurrentInventoryImportPreviewRecord>(headerMap.ErrorCode, headerMap.ErrorMessage!);
        }

        var columns = headerMap.Value!;
        int? Col(ImportColumn column) => columns.TryGetValue(column.Field, out var idx) ? idx : null;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var rows = new List<RowWork>();
        foreach (var row in ExcelWorkbookReader.GetDataRows(worksheet))
        {
            // A row with nothing in any import column is not input — e.g. the
            // Excel Table's initial empty row, or a row Excel keeps only for
            // its formatting. Never a blank-row error or a fake record.
            if (Columns.All(c => Col(c) is not { } idx || (!row.Cell(idx).HasFormula && string.IsNullOrWhiteSpace(row.Cell(idx).GetString()))))
            {
                continue;
            }

            var formulaFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var column in Columns)
            {
                if (ExcelWorkbookReader.IsFormula(row, Col(column)))
                {
                    formulaFields.Add(column.Field);
                }
            }

            var issues = new List<ImportIssue>();
            string? Text(ImportColumn column)
            {
                var value = excelReader.ReadText(row, Col(column), out var tooLong);
                AddTooLong(issues, column, value, tooLong);
                return value;
            }

            string? Identifier(ImportColumn column, out bool ambiguous)
            {
                var value = excelReader.ReadIdentifierText(row, Col(column), out ambiguous, out var tooLong);
                AddTooLong(issues, column, value, tooLong);
                return value;
            }

            var input = new CurrentInventoryImportRowInput(
                row.RowNumber(),
                Text(CurrentInventoryImportColumns.EquipmentType),
                Text(CurrentInventoryImportColumns.Manufacturer),
                Text(CurrentInventoryImportColumns.Model),
                Identifier(CurrentInventoryImportColumns.SerialNumber, out var serialAmbiguous), serialAmbiguous,
                Text(CurrentInventoryImportColumns.Notes),
                Text(CurrentInventoryImportColumns.Processor),
                Text(CurrentInventoryImportColumns.Memory),
                Text(CurrentInventoryImportColumns.Storage),
                Identifier(CurrentInventoryImportColumns.Imei, out var imeiAmbiguous), imeiAmbiguous,
                Identifier(CurrentInventoryImportColumns.MobileNumber, out var mobileAmbiguous), mobileAmbiguous,
                Identifier(CurrentInventoryImportColumns.SimSerialNumber, out var simAmbiguous), simAmbiguous,
                Text(CurrentInventoryImportColumns.FullName),
                Identifier(CurrentInventoryImportColumns.Nif, out var nifAmbiguous), nifAmbiguous,
                Text(CurrentInventoryImportColumns.Email),
                Identifier(CurrentInventoryImportColumns.Phone, out var phoneAmbiguous), phoneAmbiguous,
                Text(CurrentInventoryImportColumns.Address),
                Text(CurrentInventoryImportColumns.WorkforceType),
                Text(CurrentInventoryImportColumns.EngagementType),
                Text(CurrentInventoryImportColumns.RoleOrFunction),
                Text(CurrentInventoryImportColumns.Company),
                ExcelWorkbookReader.ReadDate(row, Col(CurrentInventoryImportColumns.AllocationStartDate), out var startInvalid), startInvalid,
                ExcelWorkbookReader.ReadDate(row, Col(CurrentInventoryImportColumns.AllocationEndDate), out var endInvalid), endInvalid,
                ExcelWorkbookReader.ReadDate(row, Col(CurrentInventoryImportColumns.AssignmentDate), out var assignmentInvalid), assignmentInvalid,
                formulaFields);

            var fields = CurrentInventoryImportRowValidator.ValidateStructural(input, today, issues);
            rows.Add(new RowWork(row.RowNumber(), issues, fields));
        }

        var snapshots = await EvaluateAsync(rows, cancellationToken);

        var session = new ImportSession
        {
            Id = Guid.NewGuid(),
            ImportType = ImportType.CurrentInventory,
            Status = ImportSessionStatus.Pending,
            OriginalFileName = fileName,
            FileSha256 = Convert.ToHexStringLower(SHA256.HashData(fileBytes)),
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
    /// Every database-dependent and cross-row rule, shared by preview and by
    /// confirmation's re-validation (which re-runs it inside the confirmation
    /// transaction against the then-current data). Never writes anything.
    /// </summary>
    private async Task<List<CurrentInventoryImportRowSnapshot>> EvaluateAsync(List<RowWork> rows, CancellationToken cancellationToken)
    {
        // --- Equipment identity: in the file, then in the database ---
        FlagInFileDuplicates(rows, r => r.Fields.NormalizedSerialNumber, CurrentInventoryImportIssueCodes.DuplicateSerialInFile, CurrentInventoryImportColumns.SerialNumber);
        FlagInFileDuplicates(rows, r => r.Fields.NormalizedImei, CurrentInventoryImportIssueCodes.DuplicateImeiInFile, CurrentInventoryImportColumns.Imei);

        var candidateSerials = rows.Select(r => r.Fields.NormalizedSerialNumber).OfType<string>().Distinct().ToList();
        var existingSerials = candidateSerials.Count == 0
            ? []
            : (await dbContext.Equipment.AsNoTracking()
                .Where(e => e.NormalizedSerialNumber != null && candidateSerials.Contains(e.NormalizedSerialNumber))
                .Select(e => e.NormalizedSerialNumber!).ToListAsync(cancellationToken)).ToHashSet();

        var candidateImeis = rows.Select(r => r.Fields.NormalizedImei).OfType<string>().Distinct().ToList();
        var existingImeis = candidateImeis.Count == 0
            ? []
            : (await dbContext.MobilePhoneSpecifications.AsNoTracking()
                .Where(s => candidateImeis.Contains(s.NormalizedImei))
                .Select(s => s.NormalizedImei).ToListAsync(cancellationToken)).ToHashSet();

        foreach (var row in rows)
        {
            if (row.Fields.NormalizedSerialNumber is { } serial && existingSerials.Contains(serial))
            {
                row.Issues.Add(Error(CurrentInventoryImportIssueCodes.DuplicateSerialExisting, CurrentInventoryImportColumns.SerialNumber,
                    $"Já existe um equipamento com o valor indicado em \"{CurrentInventoryImportColumns.SerialNumber.Header}\". Esta importação só cria equipamentos novos e não altera equipamentos existentes."));
            }

            if (row.Fields.NormalizedImei is { } imei && existingImeis.Contains(imei))
            {
                row.Issues.Add(Error(CurrentInventoryImportIssueCodes.DuplicateImeiExisting, CurrentInventoryImportColumns.Imei,
                    $"Já existe um telemóvel com o valor indicado em \"{CurrentInventoryImportColumns.Imei.Header}\". Esta importação só cria equipamentos novos e não altera equipamentos existentes."));
            }
        }

        // --- Equipment types (by name, then every rule by Kind) ---
        var candidateTypeNames = rows.Where(r => r.Fields.EquipmentTypeName.Length > 0)
            .Select(r => EquipmentTypeRules.NormalizeName(r.Fields.EquipmentTypeName)).Distinct().ToList();
        var types = candidateTypeNames.Count == 0
            ? []
            : await dbContext.EquipmentTypes.AsNoTracking().Where(t => candidateTypeNames.Contains(t.NormalizedName))
                .Select(t => new { t.Id, t.NormalizedName, t.IsActive, t.Kind }).ToListAsync(cancellationToken);
        var typeByName = types.ToDictionary(t => t.NormalizedName);

        var typeOfRow = new Dictionary<int, (Guid Id, EquipmentTypeKind Kind)>();
        foreach (var row in rows.Where(r => r.Fields.EquipmentTypeName.Length > 0))
        {
            if (!typeByName.TryGetValue(EquipmentTypeRules.NormalizeName(row.Fields.EquipmentTypeName), out var type))
            {
                row.Issues.Add(Error(CurrentInventoryImportIssueCodes.UnknownEquipmentType, CurrentInventoryImportColumns.EquipmentType,
                    $"O valor indicado em \"{CurrentInventoryImportColumns.EquipmentType.Header}\" não é válido: não existe nenhum tipo de equipamento com este nome."));
            }
            else if (!type.IsActive)
            {
                row.Issues.Add(Error(CurrentInventoryImportIssueCodes.InactiveEquipmentType, CurrentInventoryImportColumns.EquipmentType,
                    $"O tipo de equipamento indicado em \"{CurrentInventoryImportColumns.EquipmentType.Header}\" está inativo."));
            }
            else
            {
                typeOfRow[row.RowNumber] = (type.Id, type.Kind);
                CurrentInventoryImportRowValidator.ValidateAgainstKind(type.Kind, row.Fields, row.Issues);
            }
        }

        // --- Companies (Externo only — Interno never carries one) ---
        var candidateCompanies = rows.Where(r => r.Fields.HasConsultant && r.Fields.CompanyName is not null)
            .Select(r => CompanyRules.NormalizeName(r.Fields.CompanyName!)).Distinct().ToList();
        var companies = candidateCompanies.Count == 0
            ? []
            : await dbContext.Companies.AsNoTracking().Where(c => candidateCompanies.Contains(c.NormalizedName))
                .Select(c => new { c.Id, c.NormalizedName, c.IsActive }).ToListAsync(cancellationToken);
        var companyByName = companies.ToDictionary(c => c.NormalizedName);

        var companyOfRow = new Dictionary<int, Guid>();
        foreach (var row in rows.Where(r => r.Fields.HasConsultant && r.Fields.CompanyName is not null && r.Fields.WorkforceType == WorkforceType.External))
        {
            if (!companyByName.TryGetValue(CompanyRules.NormalizeName(row.Fields.CompanyName!), out var company))
            {
                row.Issues.Add(Error(CurrentInventoryImportIssueCodes.UnknownCompany, CurrentInventoryImportColumns.Company,
                    $"Não foi possível encontrar a empresa indicada em \"{CurrentInventoryImportColumns.Company.Header}\". As empresas não são criadas pela importação."));
            }
            else if (!company.IsActive)
            {
                row.Issues.Add(Error(CurrentInventoryImportIssueCodes.InactiveCompany, CurrentInventoryImportColumns.Company,
                    $"A empresa indicada em \"{CurrentInventoryImportColumns.Company.Header}\" está inativa."));
            }
            else
            {
                companyOfRow[row.RowNumber] = company.Id;
            }
        }

        // --- People: one Consultant per NIF ---
        var groups = rows.Where(r => r.Fields.HasConsultant && r.Fields.Nif is not null).GroupBy(r => r.Fields.Nif!).ToList();
        var nifs = groups.Select(g => g.Key).ToList();
        var consultants = nifs.Count == 0
            ? []
            : await dbContext.Consultants.AsNoTracking().Where(c => nifs.Contains(c.Nif))
                .Select(c => new { c.Id, c.Nif, c.FullName, c.Email, c.Phone, c.Address, c.IsActive }).ToListAsync(cancellationToken);
        var consultantByNif = consultants.ToDictionary(c => c.Nif);

        var consultantIds = consultants.Select(c => c.Id).ToList();
        var activeAllocations = consultantIds.Count == 0
            ? []
            : await dbContext.ConsultantAllocations.AsNoTracking()
                .Where(a => consultantIds.Contains(a.ConsultantId) && a.Status == AllocationStatus.Active)
                .Select(a => new { a.Id, a.ConsultantId, a.WorkforceType, a.EngagementType, a.RoleOrFunction, a.CompanyId, a.StartDate, a.EndDate })
                .ToListAsync(cancellationToken);
        var activeByConsultant = activeAllocations.ToLookup(a => a.ConsultantId);

        var planByNif = new Dictionary<string, PersonPlan>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var groupRows = group.ToList();

            // Repeated rows of one person must say the same thing — never let
            // whichever row happens to be processed last win.
            var differing = PersonFields
                .Where(p => groupRows.Select(r => p.Key(r.Fields)).Distinct(StringComparer.Ordinal).Count() > 1)
                .Select(p => p.Column.Header).ToList();
            if (differing.Count > 0)
            {
                var message =
                    $"A mesma pessoa (mesmo NIF) aparece nas linhas {string.Join(", ", groupRows.Select(r => r.RowNumber))} com dados diferentes em: " +
                    $"{string.Join(", ", differing)}. Todas as linhas da mesma pessoa têm de ter os mesmos dados de pessoa e de alocação.";
                foreach (var row in groupRows)
                {
                    row.Issues.Add(Error(CurrentInventoryImportIssueCodes.InconsistentConsultantInFile, CurrentInventoryImportColumns.Nif, message));
                }

                continue;
            }

            var fields = groupRows[0].Fields;
            companyOfRow.TryGetValue(groupRows[0].RowNumber, out var resolvedCompany);
            Guid? companyId = fields.WorkforceType == WorkforceType.External && resolvedCompany != Guid.Empty ? resolvedCompany : null;

            if (!consultantByNif.TryGetValue(group.Key, out var existing))
            {
                planByNif[group.Key] = new PersonPlan(CurrentInventoryConsultantAction.Create, null, CurrentInventoryAllocationAction.Create, null);
                continue;
            }

            var personIssues = new List<ImportIssue>();
            if (!existing.IsActive)
            {
                personIssues.Add(Error(CurrentInventoryImportIssueCodes.ConsultantArchived, CurrentInventoryImportColumns.Nif,
                    "O consultor com este NIF está arquivado na plataforma e não pode ficar com equipamento atribuído."));
            }

            var identityDiffers = new List<string>();
            if (fields.FullName is not null && TextKey(fields.FullName) != TextKey(existing.FullName))
            {
                identityDiffers.Add(CurrentInventoryImportColumns.FullName.Header);
            }

            if (fields.Email is not null && TextKey(fields.Email) != TextKey(existing.Email))
            {
                identityDiffers.Add(CurrentInventoryImportColumns.Email.Header);
            }

            if (identityDiffers.Count > 0)
            {
                personIssues.Add(Error(CurrentInventoryImportIssueCodes.ExistingConsultantMismatch, CurrentInventoryImportColumns.Nif,
                    $"Já existe um consultor com este NIF, mas com outro valor em: {string.Join(", ", identityDiffers)}. " +
                    "A importação não altera consultores existentes: corrija o ficheiro ou atualize o consultor na plataforma."));
            }

            var contactDiffers = new List<string>();
            if (fields.Phone is not null && PhoneKey(fields.Phone) != PhoneKey(existing.Phone))
            {
                contactDiffers.Add(CurrentInventoryImportColumns.Phone.Header);
            }

            if (fields.Address is not null && TextKey(fields.Address) != TextKey(existing.Address))
            {
                contactDiffers.Add(CurrentInventoryImportColumns.Address.Header);
            }

            if (contactDiffers.Count > 0)
            {
                personIssues.Add(new ImportIssue(CurrentInventoryImportIssueCodes.ExistingConsultantContactDiffers, CurrentInventoryImportColumns.Nif.Field,
                    $"O consultor já existe na plataforma com outro valor em: {string.Join(", ", contactDiffers)}. Os dados registados mantêm-se e não são alterados pela importação.",
                    ImportIssueSeverity.Warning));
            }

            var active = activeByConsultant[existing.Id].ToList();
            var allocationAction = CurrentInventoryAllocationAction.Create;
            Guid? existingAllocationId = null;
            if (active.Count > 1)
            {
                // Inconsistent legacy data: never guess which one applies.
                personIssues.Add(Error(CurrentInventoryImportIssueCodes.MultipleActiveAllocations, CurrentInventoryImportColumns.Nif,
                    "O consultor com este NIF tem mais do que uma alocação ativa na plataforma. Corrija as alocações do consultor antes de importar."));
            }
            else if (active.Count == 1)
            {
                var allocation = active[0];
                if (fields.WorkforceType is { } workforceType && workforceType != allocation.WorkforceType)
                {
                    personIssues.Add(Error(CurrentInventoryImportIssueCodes.ExistingRegimeConflict, CurrentInventoryImportColumns.WorkforceType,
                        $"O consultor com este NIF tem na plataforma uma alocação ativa com o regime {ExcelDisplayValues.Label(allocation.WorkforceType)}, " +
                        $"e o ficheiro indica {ExcelDisplayValues.Label(workforceType)}. A importação não altera o regime: corrija o ficheiro ou termine a alocação atual na plataforma."));
                }
                else
                {
                    var allocationDiffers = new List<string>();
                    if (fields.EngagementType is { } engagementType && engagementType != allocation.EngagementType)
                    {
                        allocationDiffers.Add(CurrentInventoryImportColumns.EngagementType.Header);
                    }

                    if (fields.RoleOrFunction is not null && TextKey(fields.RoleOrFunction) != TextKey(allocation.RoleOrFunction))
                    {
                        allocationDiffers.Add(CurrentInventoryImportColumns.RoleOrFunction.Header);
                    }

                    // Only compared once the workbook's company resolved (an
                    // unknown company is already its own error).
                    var companyResolved = fields.WorkforceType == WorkforceType.Internal || companyId is not null;
                    if (companyResolved && companyId != allocation.CompanyId)
                    {
                        allocationDiffers.Add(CurrentInventoryImportColumns.Company.Header);
                    }

                    if (fields.AllocationStartDate is { } start && start != allocation.StartDate)
                    {
                        allocationDiffers.Add(CurrentInventoryImportColumns.AllocationStartDate.Header);
                    }

                    if (fields.AllocationEndDate != allocation.EndDate)
                    {
                        allocationDiffers.Add(CurrentInventoryImportColumns.AllocationEndDate.Header);
                    }

                    if (allocationDiffers.Count > 0)
                    {
                        personIssues.Add(Error(CurrentInventoryImportIssueCodes.ExistingAllocationConflict, CurrentInventoryImportColumns.Nif,
                            $"A alocação ativa deste consultor na plataforma é diferente da indicada no ficheiro em: {string.Join(", ", allocationDiffers)}. " +
                            "A importação não altera alocações existentes: corrija o ficheiro ou atualize a alocação na plataforma."));
                    }
                    else
                    {
                        allocationAction = CurrentInventoryAllocationAction.Reuse;
                        existingAllocationId = allocation.Id;
                    }
                }
            }

            foreach (var row in groupRows)
            {
                row.Issues.AddRange(personIssues);
            }

            planByNif[group.Key] = new PersonPlan(CurrentInventoryConsultantAction.Reuse, existing.Id, allocationAction, existingAllocationId);
        }

        var snapshots = new List<CurrentInventoryImportRowSnapshot>(rows.Count);
        foreach (var row in rows)
        {
            var f = row.Fields;
            var hasType = typeOfRow.TryGetValue(row.RowNumber, out var type);
            var hasCompany = companyOfRow.TryGetValue(row.RowNumber, out var companyId);
            var plan = f.HasConsultant && f.Nif is not null && planByNif.TryGetValue(f.Nif, out var p) ? p : null;

            snapshots.Add(new CurrentInventoryImportRowSnapshot(
                row.RowNumber, ImportRowStatusCalculator.Compute(row.Issues),
                f.EquipmentTypeName, hasType ? type.Id : null, hasType ? type.Kind : null,
                f.Manufacturer, f.Model, f.SerialNumber, f.Notes, f.Processor, f.Memory, f.Storage,
                f.Imei, f.MobileNumber, f.SimSerialNumber,
                f.HasConsultant, f.FullName, f.Nif, f.Email, f.Phone, f.Address,
                f.WorkforceType, f.EngagementType, f.RoleOrFunction, f.CompanyName, hasCompany ? companyId : null,
                f.AllocationStartDate, f.AllocationEndDate, f.AssignmentDate,
                plan?.ConsultantAction ?? CurrentInventoryConsultantAction.None, plan?.ExistingConsultantId,
                plan?.AllocationAction ?? CurrentInventoryAllocationAction.None, plan?.ExistingAllocationId,
                row.Issues));
        }

        return snapshots;
    }

    // ------------------------------------------------------------------
    // Session read / cancel
    // ------------------------------------------------------------------

    /// <summary>Also an access point for lazy PII cleanup, exactly like the other imports: an expired Pending session has its snapshot cleared here.</summary>
    public async Task<CurrentInventoryImportPreviewRecord?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.ImportSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.ImportType == ImportType.CurrentInventory, cancellationToken);
        if (session is null)
        {
            return null;
        }

        await ClearIfExpiredPendingAsync(session, cancellationToken);
        return ToPreviewRecord(session, DeserializeRows(session.RowsJson));
    }

    public async Task<ServiceResult<CurrentInventoryImportPreviewRecord>> CancelAsync(
        Guid sessionId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.ImportSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.ImportType == ImportType.CurrentInventory, cancellationToken);
        if (session is null)
        {
            return ServiceResult.Fail<CurrentInventoryImportPreviewRecord>(ServiceErrorCode.NotFound, ImportSessionMessages.NotFound);
        }

        if (!ImportSessionRules.CanCancel(session.Status))
        {
            return ServiceResult.Fail<CurrentInventoryImportPreviewRecord>(ServiceErrorCode.ImportSessionNotConfirmable, ImportSessionMessages.NotCancellable);
        }

        session.Status = ImportSessionStatus.Cancelled;
        session.RowsJson = null;

        await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.CurrentInventoryImport.Cancelled,
            EntityType: "ImportSession",
            EntityId: session.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { ImportSessionId = session.Id, session.FileSha256 }), cancellationToken);
        await transaction.CommitIfOwnedAsync(cancellationToken);

        return ServiceResult.Ok(ToPreviewRecord(session, []));
    }

    // ------------------------------------------------------------------
    // Confirm
    // ------------------------------------------------------------------

    /// <summary>
    /// Establishes the previewed state atomically, or nothing at all. Runs
    /// Serializable — like Handover finalization, the other operation that
    /// creates custody — because it reuses existing Consultants/Allocations
    /// that other requests can change concurrently; every assumption from the
    /// preview (types and their kind, companies, serial/IMEI uniqueness,
    /// whether each Consultant exists, its Active allocation) is re-derived
    /// inside the transaction by the same rules. Any difference rolls back
    /// and returns the freshly re-validated rows (the session stays Pending,
    /// so it can be confirmed once the data is fixed). The unique indexes
    /// (NIF, serial, IMEI, one Active allocation per Consultant, one Active
    /// assignment per Equipment) remain the final backstop.
    /// </summary>
    public async Task<(ServiceResult<CurrentInventoryImportConfirmationSummary> Result, IReadOnlyList<CurrentInventoryImportRowSnapshot>? ConflictRows)> ConfirmAsync(
        Guid sessionId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.ImportSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.ImportType == ImportType.CurrentInventory, cancellationToken);
        if (session is null)
        {
            return (ServiceResult.Fail<CurrentInventoryImportConfirmationSummary>(ServiceErrorCode.NotFound, ImportSessionMessages.NotFound), null);
        }

        if (session.ErrorRows > 0)
        {
            return (ServiceResult.Fail<CurrentInventoryImportConfirmationSummary>(ServiceErrorCode.ImportPreviewHasErrors, ImportSessionMessages.HasErrors), null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (ImportSessionRules.IsExpiredPending(session.Status, session.ExpiresAtUtc, now))
        {
            await ClearIfExpiredPendingAsync(session, cancellationToken);
            return (ServiceResult.Fail<CurrentInventoryImportConfirmationSummary>(ServiceErrorCode.ImportSessionExpired, ImportSessionMessages.Expired), null);
        }

        if (!ImportSessionRules.CanConfirm(session.Status, session.ErrorRows, session.ExpiresAtUtc, now))
        {
            return (ServiceResult.Fail<CurrentInventoryImportConfirmationSummary>(ServiceErrorCode.ImportSessionNotConfirmable, ImportSessionMessages.NotConfirmable), null);
        }

        var previewed = DeserializeRows(session.RowsJson);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var fresh = await EvaluateAsync([.. previewed.Select(ToWork)], cancellationToken);
            var conflicts = FindStaleRows(previewed, fresh);
            if (conflicts.Count > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (ServiceResult.Fail<CurrentInventoryImportConfirmationSummary>(ServiceErrorCode.ImportPreviewStale, ImportSessionMessages.Stale), conflicts);
            }

            var outcome = await ApplyAsync(session, fresh, actorUserId, actorDisplayName, now, cancellationToken);
            if (outcome is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (ServiceResult.Fail<CurrentInventoryImportConfirmationSummary>(ServiceErrorCode.ImportPreviewStale, ImportSessionMessages.Conflict), null);
            }

            await transaction.CommitAsync(cancellationToken);
            return (ServiceResult.Ok(outcome), null);
        }
        catch (Exception ex) when (PostgresConflictClassifier.Find(
            ex, PostgresErrorCodes.UniqueViolation, PostgresErrorCodes.SerializationFailure) is not null)
        {
            // A concurrent change the re-validation above could not see yet
            // (or a serialization conflict with it): nothing was imported.
            await transaction.RollbackAsync(CancellationToken.None);
            return (ServiceResult.Fail<CurrentInventoryImportConfirmationSummary>(ServiceErrorCode.ImportPreviewStale, ImportSessionMessages.Conflict), null);
        }
    }

    /// <summary>
    /// Compares the re-validated rows with what was previewed. A row is stale
    /// if it now has an error, or if any decision it was previewed with
    /// (resolved type and kind, company, create-vs-reuse of the Consultant or
    /// Allocation and which one) would now be different — the user confirmed
    /// a specific plan, never "whatever the data says now". Returns the full
    /// re-validated row set when anything is stale, otherwise an empty list.
    /// </summary>
    private static List<CurrentInventoryImportRowSnapshot> FindStaleRows(
        List<CurrentInventoryImportRowSnapshot> previewed, List<CurrentInventoryImportRowSnapshot> fresh)
    {
        var result = new List<CurrentInventoryImportRowSnapshot>(fresh.Count);
        var anyStale = false;
        for (var i = 0; i < fresh.Count; i++)
        {
            var before = previewed[i];
            var now = fresh[i];
            var issues = now.Issues.ToList();

            if (now.Status != ImportRowStatus.Error)
            {
                if (before.EquipmentTypeId != now.EquipmentTypeId || before.EquipmentTypeKind != now.EquipmentTypeKind)
                {
                    issues.Add(Error(CurrentInventoryImportIssueCodes.EquipmentTypeChanged, CurrentInventoryImportColumns.EquipmentType,
                        "O tipo de equipamento mudou na plataforma desde a pré-visualização."));
                }

                if (before.ConsultantAction != now.ConsultantAction || before.ExistingConsultantId != now.ExistingConsultantId)
                {
                    issues.Add(Error(CurrentInventoryImportIssueCodes.ConsultantChanged, CurrentInventoryImportColumns.Nif,
                        "O consultor com este NIF foi criado ou alterado na plataforma desde a pré-visualização."));
                }

                if (before.AllocationAction != now.AllocationAction || before.ExistingAllocationId != now.ExistingAllocationId ||
                    before.CompanyId != now.CompanyId)
                {
                    issues.Add(Error(CurrentInventoryImportIssueCodes.AllocationChanged, CurrentInventoryImportColumns.WorkforceType,
                        "A alocação ativa deste consultor mudou na plataforma desde a pré-visualização."));
                }
            }

            var status = ImportRowStatusCalculator.Compute(issues);
            anyStale |= status == ImportRowStatus.Error;
            result.Add(now with { Status = status, Issues = issues });
        }

        return anyStale ? result : [];
    }

    /// <summary>
    /// Writes the plan: Consultants and Allocations first (once per NIF), then
    /// every Equipment through <c>EquipmentService.CreateAsync</c> — the normal
    /// creation path, including its own AssetTag allocation from the shared
    /// database sequence — and, for assigned rows, the Active assignment.
    /// Returns null if equipment creation refused a row (a concurrent
    /// duplicate the re-validation could not see); the caller rolls back.
    /// </summary>
    private async Task<CurrentInventoryImportConfirmationSummary?> ApplyAsync(
        ImportSession session, List<CurrentInventoryImportRowSnapshot> rows, Guid actorUserId, string actorDisplayName,
        DateTime now, CancellationToken cancellationToken)
    {
        var consultantIdByNif = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var createdConsultantIds = new List<Guid>();
        var createdAllocationIds = new List<Guid>();
        var reusedConsultants = 0;
        var reusedAllocations = 0;
        var internalPeople = 0;
        var externalPeople = 0;

        foreach (var person in rows.Where(r => r.HasConsultant).GroupBy(r => r.Nif!, StringComparer.Ordinal))
        {
            var row = person.First();
            Guid consultantId;
            if (row.ConsultantAction == CurrentInventoryConsultantAction.Reuse)
            {
                consultantId = row.ExistingConsultantId!.Value;
                reusedConsultants++;
            }
            else
            {
                // Same values ConsultantService.CreateAsync writes; inlined (as
                // in the Consultant import) so it joins this transaction.
                var consultant = new Consultant
                {
                    Id = Guid.NewGuid(),
                    FullName = row.FullName!,
                    Nif = row.Nif!,
                    Email = row.Email!,
                    Address = row.Address!,
                    Phone = row.Phone!,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                dbContext.Consultants.Add(consultant);
                consultantId = consultant.Id;
                createdConsultantIds.Add(consultant.Id);
            }

            consultantIdByNif[person.Key] = consultantId;

            if (row.AllocationAction == CurrentInventoryAllocationAction.Reuse)
            {
                reusedAllocations++;
            }
            else
            {
                // The re-validation above established there is no Active
                // allocation for this Consultant (AllocationRules.CanCreateActiveAllocation);
                // the partial unique index backs that up against a concurrent one.
                var allocation = new ConsultantAllocation
                {
                    Id = Guid.NewGuid(),
                    ConsultantId = consultantId,
                    CompanyId = row.WorkforceType == WorkforceType.External ? row.CompanyId : null,
                    WorkforceType = row.WorkforceType!.Value,
                    EngagementType = row.EngagementType!.Value,
                    RoleOrFunction = row.RoleOrFunction!,
                    StartDate = row.AllocationStartDate!.Value,
                    EndDate = row.AllocationEndDate,
                    Status = AllocationStatus.Active,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                dbContext.ConsultantAllocations.Add(allocation);
                createdAllocationIds.Add(allocation.Id);
            }

            if (row.WorkforceType == WorkforceType.Internal)
            {
                internalPeople++;
            }
            else
            {
                externalPeople++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var consultantId in createdConsultantIds)
        {
            var fullName = rows.First(r => r.HasConsultant && consultantIdByNif[r.Nif!] == consultantId).FullName;
            await auditService.RecordAsync(new AuditEntry(
                AuditActions.Consultant.Created,
                EntityType: "Consultant",
                EntityId: consultantId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { FullName = fullName }), cancellationToken);
        }

        var createdEquipment = new List<(Guid Id, string AssetTag)>();
        var createdAssignmentIds = new List<Guid>();
        foreach (var row in rows)
        {
            var laptop = row.EquipmentTypeKind == EquipmentTypeKind.Laptop
                ? new LaptopSpecificationRecord(row.Processor!, row.Memory!, row.Storage!)
                : null;
            var mobile = row.EquipmentTypeKind == EquipmentTypeKind.MobilePhone
                ? new MobilePhoneSpecificationRecord(row.Imei!, row.MobileNumber, row.SimSerialNumber)
                : null;

            // Migration-origin exception, deliberately scoped to this import:
            // existing Laptops/Mobile Phones are recorded without the official
            // condition photos (they are pre-existing, already-held equipment —
            // no photo is fabricated). Normal creation still requires them; the
            // photos can be established later via the legacy backfill action.
            var created = await equipmentService.CreateAsync(
                new CreateEquipmentData(row.EquipmentTypeId!.Value, row.Manufacturer, row.Model, row.SerialNumber, row.Notes, laptop,
                    MobilePhoneSpecification: mobile),
                photos: [], actorUserId, actorDisplayName, skipConditionPhotoRequirement: true, cancellationToken);
            if (!created.Succeeded)
            {
                return null;
            }

            createdEquipment.Add((created.Value!.Id, created.Value.AssetTag));

            if (!row.HasConsultant)
            {
                continue;
            }

            var equipment = await dbContext.Equipment.FirstAsync(e => e.Id == created.Value.Id, cancellationToken);
            equipment.Status = EquipmentStatus.Assigned;
            equipment.UpdatedAtUtc = now;

            var consultantId = consultantIdByNif[row.Nif!];
            var assignment = new EquipmentAssignment
            {
                Id = Guid.NewGuid(),
                EquipmentId = equipment.Id,
                ConsultantId = consultantId,
                Origin = EquipmentAssignmentOrigin.CurrentInventoryImport,
                EquipmentHandoverId = null,
                EquipmentHandoverItemId = null,
                Status = AssignmentStatus.Active,
                // The real-world start of the current custody: the supplied
                // date at 00:00 UTC (no time of day is invented), or — when
                // none was given — the moment it was recorded.
                StartedAtUtc = row.AssignmentDate is { } date ? date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            dbContext.EquipmentAssignments.Add(assignment);
            await dbContext.SaveChangesAsync(cancellationToken);
            createdAssignmentIds.Add(assignment.Id);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.EquipmentAssignment.Started,
                EntityType: "Equipment",
                EntityId: equipment.Id.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new
                {
                    equipment.AssetTag,
                    ConsultantId = consultantId,
                    Origin = nameof(EquipmentAssignmentOrigin.CurrentInventoryImport),
                    ImportSessionId = session.Id,
                }), cancellationToken);
        }

        session.Status = ImportSessionStatus.Completed;
        session.ConfirmedByUserId = actorUserId;
        session.ConfirmedByDisplayNameSnapshot = actorDisplayName;
        session.ConfirmedAtUtc = now;

        // The detailed PII snapshot is no longer needed once the import has
        // landed — cleared in the same commit as completion, like the others.
        session.RowsJson = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.CurrentInventoryImport.Completed,
            EntityType: "ImportSession",
            EntityId: session.Id.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new
            {
                ImportSessionId = session.Id,
                ImportOrigin = nameof(EquipmentAssignmentOrigin.CurrentInventoryImport),
                session.FileSha256,
                RowCount = rows.Count,
                CreatedConsultantCount = createdConsultantIds.Count,
                ReusedConsultantCount = reusedConsultants,
                CreatedAllocationCount = createdAllocationIds.Count,
                ReusedAllocationCount = reusedAllocations,
                CreatedEquipmentCount = createdEquipment.Count,
                CreatedAssignmentCount = createdAssignmentIds.Count,
                AvailableEquipmentCount = createdEquipment.Count - createdAssignmentIds.Count,
                InternalConsultantCount = internalPeople,
                ExternalConsultantCount = externalPeople,
                CreatedConsultantIds = createdConsultantIds,
                CreatedAllocationIds = createdAllocationIds,
                CreatedEquipmentIds = createdEquipment.Select(e => e.Id).ToList(),
                CreatedAssignmentIds = createdAssignmentIds,
            }), cancellationToken);

        return new CurrentInventoryImportConfirmationSummary(
            session.Id, session.Status, createdConsultantIds.Count, reusedConsultants, createdAllocationIds.Count, reusedAllocations,
            createdEquipment.Count, createdAssignmentIds.Count, now);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Back from a stored (already structurally valid) row to the evaluation input — no structural issue can exist on a confirmable row.</summary>
    private static RowWork ToWork(CurrentInventoryImportRowSnapshot s) => new(
        s.RowNumber,
        [],
        new CurrentInventoryImportRowFields(
            s.EquipmentTypeName, s.Manufacturer, s.Model, s.SerialNumber, EquipmentRules.NormalizeSerialNumber(s.SerialNumber), s.Notes,
            s.Processor, s.Memory, s.Storage, s.Imei, MobilePhoneSpecificationRules.NormalizeImei(s.Imei), s.MobileNumber, s.SimSerialNumber,
            s.HasConsultant, s.FullName, s.Nif, s.Email, s.Phone, s.Address, s.WorkforceType, s.EngagementType, s.RoleOrFunction,
            s.CompanyName, s.AllocationStartDate, s.AllocationEndDate, s.AssignmentDate));

    private static void FlagInFileDuplicates(List<RowWork> rows, Func<RowWork, string?> key, string code, ImportColumn column)
    {
        foreach (var group in rows.Where(r => key(r) is not null).GroupBy(key).Where(g => g.Count() > 1))
        {
            var message =
                $"O valor indicado em \"{column.Header}\" aparece mais do que uma vez no ficheiro (linhas {string.Join(", ", group.Select(r => r.RowNumber))}). " +
                "Cada equipamento só pode aparecer numa linha.";
            foreach (var row in group)
            {
                row.Issues.Add(Error(code, column, message));
            }
        }
    }

    public static CurrentInventoryImportSummary Summarize(IReadOnlyList<CurrentInventoryImportRowSnapshot> rows)
    {
        var importable = rows.Where(r => r.Status != ImportRowStatus.Error).ToList();
        var people = importable.Where(r => r.HasConsultant && r.Nif is not null)
            .GroupBy(r => r.Nif!, StringComparer.Ordinal).Select(g => g.First()).ToList();

        return new CurrentInventoryImportSummary(
            NewConsultants: people.Count(p => p.ConsultantAction == CurrentInventoryConsultantAction.Create),
            ReusedConsultants: people.Count(p => p.ConsultantAction == CurrentInventoryConsultantAction.Reuse),
            NewAllocations: people.Count(p => p.AllocationAction == CurrentInventoryAllocationAction.Create),
            ReusedAllocations: people.Count(p => p.AllocationAction == CurrentInventoryAllocationAction.Reuse),
            EquipmentToCreate: importable.Count,
            EquipmentToAssign: importable.Count(r => r.HasConsultant),
            EquipmentToStayAvailable: importable.Count(r => !r.HasConsultant),
            InternalConsultants: people.Count(p => p.WorkforceType == WorkforceType.Internal),
            ExternalConsultants: people.Count(p => p.WorkforceType == WorkforceType.External));
    }

    /// <summary>Whitespace-collapsed, case-insensitive comparison key.</summary>
    private static string TextKey(string? value) =>
        value is null ? string.Empty : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string PhoneKey(string? value) =>
        value is null ? string.Empty : new string([.. value.Where(c => !char.IsWhiteSpace(c))]);

    private static void AddTooLong(List<ImportIssue> issues, ImportColumn column, string? value, bool exceedsCellLimit)
    {
        var tooLong = exceedsCellLimit ||
            (value is not null && CurrentInventoryImportColumns.MaxLengths.TryGetValue(column.Field, out var max) && value.Length > max);
        if (tooLong)
        {
            issues.Add(Error(CurrentInventoryImportIssueCodes.CellTooLong, column, ImportMessages.TooLong(column.Header)));
        }
    }

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

    private static List<CurrentInventoryImportRowSnapshot> DeserializeRows(string? rowsJson) =>
        rowsJson is null ? [] : JsonSerializer.Deserialize<List<CurrentInventoryImportRowSnapshot>>(rowsJson, JsonOptions)!;

    private static CurrentInventoryImportPreviewRecord ToPreviewRecord(ImportSession session, IReadOnlyList<CurrentInventoryImportRowSnapshot> rows) => new(
        session.Id, session.OriginalFileName, session.FileSha256, session.Status,
        session.TotalRows, session.ValidRows, session.WarningRows, session.ErrorRows, session.CreatedAtUtc, Summarize(rows), rows);

    private static ImportIssue Error(string code, ImportColumn column, string message) => new(code, column.Field, message, ImportIssueSeverity.Error);
}
