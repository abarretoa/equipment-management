using ClosedXML.Excel;
using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Consultants;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Consultant Excel export (Phase 8 scope). Two worksheets —
/// current-state <c>Consultores</c> and full-history <c>Alocações</c> — so
/// history is never lossily flattened into a single "current" row (Phase 8 scope). Every user-controlled text cell is written through
/// <see cref="SafeExcelCellWriter"/> (formula-injection defense).
/// Sheet names, headers and enum values are pt-PT (ADR-070); the columns, their
/// order and the data behind them are unchanged.
/// </summary>
public sealed class ConsultantExportService(ApplicationDbContext dbContext, AuditService auditService)
{
    public const string ConsultantsSheetName = "Consultores";
    public const string AllocationsSheetName = "Alocações";

    /// <summary>The Excel Tables that carry each sheet's filter (<see cref="ExcelSheetLayout.AddFilterTable"/>).</summary>
    public const string ConsultantsTableName = "Consultores_Exportacao";
    public const string AllocationsTableName = "Alocacoes_Exportacao";

    /// <summary>
    /// The profile columns carry the same headers as the Consultant import
    /// (<see cref="ConsultantImportColumns"/>); the allocation columns are
    /// deliberately labelled as the <em>current</em> allocation, which is
    /// what they are here and what keeps them distinct from the import's
    /// initial-allocation columns.
    /// </summary>
    private static readonly string[] ConsultantsHeaders =
    [
        "ID do consultor", ConsultantImportColumns.FullName.Header, ConsultantImportColumns.Nif.Header, ConsultantImportColumns.Email.Header,
        ConsultantImportColumns.Phone.Header, ConsultantImportColumns.Address.Header, "Ativo",
        "Regime atual", "Tipo de vínculo atual", "Função atual", "Empresa / Cliente atual",
        "Início da alocação atual", "Fim da alocação atual",
    ];

    private static readonly string[] AllocationsHeaders =
    [
        "ID do consultor", "Consultor", ConsultantImportColumns.Company.Header, ConsultantImportColumns.WorkforceType.Header,
        ConsultantImportColumns.EngagementType.Header, ConsultantImportColumns.RoleOrFunction.Header,
        ConsultantImportColumns.AllocationStartDate.Header, ConsultantImportColumns.AllocationEndDate.Header, "Estado",
    ];

    private sealed record ConsultantWithCurrentAllocation(Domain.Consultants.Consultant Consultant, ConsultantAllocation? CurrentAllocation);

    public async Task<byte[]> ExportAsync(
        ConsultantListFilter filter, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Consultants.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(c => EF.Functions.ILike(c.FullName, $"%{filter.Search}%"));
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(c => c.IsActive == isActive);
        }

        var projected =
            from c in query
            join a in dbContext.ConsultantAllocations.AsNoTracking().Where(a => a.Status == AllocationStatus.Active)
                on c.Id equals a.ConsultantId into allocations
            from currentAllocation in allocations.DefaultIfEmpty()
            select new ConsultantWithCurrentAllocation(c, currentAllocation);

        if (filter.WorkforceType is { } workforceType)
        {
            projected = projected.Where(x => x.CurrentAllocation != null && x.CurrentAllocation.WorkforceType == workforceType);
        }

        if (filter.CompanyId is { } companyId)
        {
            projected = projected.Where(x => x.CurrentAllocation != null && x.CurrentAllocation.CompanyId == companyId);
        }

        // Ordering happens client-side after materialization: EF Core cannot
        // translate an ORDER BY over a member access into a client-constructed
        // record type (ConsultantWithCurrentAllocation.Consultant.FullName) —
        // the list is small enough (this application's whole Consultant
        // roster) for that to be a non-issue.
        var consultants = (await projected.ToListAsync(cancellationToken))
            .OrderBy(x => x.Consultant.FullName, StringComparer.Ordinal).ToList();

        var consultantIds = consultants.Select(x => x.Consultant.Id).ToList();
        var allAllocations = consultantIds.Count == 0
            ? []
            : await dbContext.ConsultantAllocations.AsNoTracking()
                .Where(a => consultantIds.Contains(a.ConsultantId))
                .OrderBy(a => a.StartDate)
                .ToListAsync(cancellationToken);

        var companyIds = consultants.Where(x => x.CurrentAllocation?.CompanyId is not null).Select(x => x.CurrentAllocation!.CompanyId!.Value)
            .Concat(allAllocations.Where(a => a.CompanyId is not null).Select(a => a.CompanyId!.Value))
            .Distinct().ToList();
        var companyNames = companyIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        using var workbook = new XLWorkbook();
        BuildConsultantsSheet(workbook, consultants, companyNames);
        BuildAllocationsSheet(workbook, consultants, allAllocations, companyNames);

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        var bytes = output.ToArray();

        await auditService.RecordAsync(new AuditEntry(
            Domain.Auditing.AuditActions.ConsultantExport.Generated,
            EntityType: "Consultant",
            EntityId: null,
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { RowCount = consultants.Count }), cancellationToken);

        return bytes;
    }

    private static void BuildConsultantsSheet(
        XLWorkbook workbook, IReadOnlyList<ConsultantWithCurrentAllocation> consultants, IReadOnlyDictionary<Guid, string> companyNames)
    {
        var sheet = workbook.Worksheets.Add(ConsultantsSheetName);
        WriteHeader(sheet, ConsultantsHeaders);

        var rowIndex = 2;
        foreach (var x in consultants)
        {
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 1), x.Consultant.Id.ToString());
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 2), x.Consultant.FullName);
            // NIF/Phone go through the same safe writer as every other
            // user-controlled field (Phase 8 scope) — it already
            // applies the Text number format these identifier columns need,
            // so there is no separate code path for them.
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 3), x.Consultant.Nif);
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 4), x.Consultant.Email);
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 5), x.Consultant.Phone);
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 6), x.Consultant.Address);
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 7), ExcelDisplayValues.Label(x.Consultant.IsActive));

            if (x.CurrentAllocation is { } currentAllocation)
            {
                SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 8), ExcelDisplayValues.Label(currentAllocation.WorkforceType));
                SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 9), ExcelDisplayValues.Label(currentAllocation.EngagementType));
                SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 10), currentAllocation.RoleOrFunction);
                SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 11),
                    currentAllocation.CompanyId is { } id ? companyNames.GetValueOrDefault(id) : null);
                var startCell = sheet.Cell(rowIndex, 12);
                startCell.Value = currentAllocation.StartDate.ToDateTime(TimeOnly.MinValue);
                startCell.Style.DateFormat.Format = ExcelSheetLayout.DateFormat;
                if (currentAllocation.EndDate is { } end)
                {
                    var endCell = sheet.Cell(rowIndex, 13);
                    endCell.Value = end.ToDateTime(TimeOnly.MinValue);
                    endCell.Style.DateFormat.Format = ExcelSheetLayout.DateFormat;
                }
            }

            rowIndex++;
        }

        FinishSheet(sheet, ConsultantsTableName, ConsultantsHeaders.Length, rowIndex - 1);
    }

    private static void BuildAllocationsSheet(
        XLWorkbook workbook, IReadOnlyList<ConsultantWithCurrentAllocation> consultants, IReadOnlyList<ConsultantAllocation> allocations,
        IReadOnlyDictionary<Guid, string> companyNames)
    {
        var sheet = workbook.Worksheets.Add(AllocationsSheetName);
        WriteHeader(sheet, AllocationsHeaders);

        var consultantNames = consultants.ToDictionary(x => x.Consultant.Id, x => x.Consultant.FullName);

        var rowIndex = 2;
        foreach (var allocation in allocations)
        {
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 1), allocation.ConsultantId.ToString());
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 2), consultantNames.GetValueOrDefault(allocation.ConsultantId));
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 3), allocation.CompanyId is { } id ? companyNames.GetValueOrDefault(id) : null);
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 4), ExcelDisplayValues.Label(allocation.WorkforceType));
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 5), ExcelDisplayValues.Label(allocation.EngagementType));
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 6), allocation.RoleOrFunction);

            var startCell = sheet.Cell(rowIndex, 7);
            startCell.Value = allocation.StartDate.ToDateTime(TimeOnly.MinValue);
            startCell.Style.DateFormat.Format = ExcelSheetLayout.DateFormat;
            if (allocation.EndDate is { } end)
            {
                var endCell = sheet.Cell(rowIndex, 8);
                endCell.Value = end.ToDateTime(TimeOnly.MinValue);
                endCell.Style.DateFormat.Format = ExcelSheetLayout.DateFormat;
            }

            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 9), ExcelDisplayValues.Label(allocation.Status));
            rowIndex++;
        }

        FinishSheet(sheet, AllocationsTableName, AllocationsHeaders.Length, rowIndex - 1);
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers) =>
        ExcelSheetLayout.WriteHeaderRow(sheet, headers, _ => true);

    /// <summary>Readable widths and a filter on the header row — this is a list people sort and filter.</summary>
    private static void FinishSheet(IXLWorksheet sheet, string tableName, int columnCount, int lastRow)
    {
        ExcelSheetLayout.FitColumns(sheet, columnCount);
        ExcelSheetLayout.AddFilterTable(sheet, tableName, columnCount, lastRow);
    }
}
