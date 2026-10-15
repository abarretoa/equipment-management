using ClosedXML.Excel;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Equipments;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// Equipment Excel export (Phase 8 scope). Current custody
/// comes exclusively from an <see cref="AssignmentStatus.Active"/>
/// <see cref="EquipmentAssignment"/> row — never inferred from historical
/// Handovers, and never a denormalized "current consultant" column on
/// <c>Equipment</c> itself (Phase 8 scope). Every
/// user-controlled text cell goes through <see cref="SafeExcelCellWriter"/>.
/// The sheet name, headers and enum values are pt-PT (ADR-070); the columns,
/// their order and the data behind them are unchanged.
/// </summary>
public sealed class EquipmentExportService(ApplicationDbContext dbContext, AuditService auditService)
{
    public const string SheetName = "Equipamentos";

    /// <summary>The Excel Table that carries the sheet's filter (<see cref="ExcelSheetLayout.AddFilterTable"/>).</summary>
    public const string TableName = "Equipamentos_Exportacao";

    /// <summary>Columns shared with the Equipment import carry the same headers (<see cref="EquipmentImportColumns"/>).</summary>
    private static readonly string[] Headers =
    [
        "ID do equipamento", "Código do equipamento", EquipmentImportColumns.EquipmentType.Header, "Categoria",
        EquipmentImportColumns.Manufacturer.Header, EquipmentImportColumns.Model.Header, EquipmentImportColumns.SerialNumber.Header, "Estado",
        EquipmentImportColumns.Processor.Header, EquipmentImportColumns.Memory.Header, EquipmentImportColumns.Storage.Header,
        "Consultor atual", "Início da atribuição atual",
    ];

    public async Task<byte[]> ExportAsync(
        EquipmentListFilter filter, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var query =
            from e in dbContext.Equipment.AsNoTracking()
            join t in dbContext.EquipmentTypes.AsNoTracking() on e.EquipmentTypeId equals t.Id
            select new { Equipment = e, Type = t };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Equipment.AssetTag, $"%{filter.Search}%") ||
                (x.Equipment.SerialNumber != null && EF.Functions.ILike(x.Equipment.SerialNumber, $"%{filter.Search}%")));
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

        var items = await query.OrderBy(x => x.Equipment.AssetTag).ToListAsync(cancellationToken);

        var equipmentIds = items.Select(x => x.Equipment.Id).ToList();
        var specs = equipmentIds.Count == 0
            ? []
            : await dbContext.LaptopSpecifications.AsNoTracking().Where(s => equipmentIds.Contains(s.EquipmentId)).ToListAsync(cancellationToken);
        var specsByEquipmentId = specs.ToDictionary(s => s.EquipmentId);

        var activeAssignments = equipmentIds.Count == 0
            ? []
            : await dbContext.EquipmentAssignments.AsNoTracking()
                .Where(a => equipmentIds.Contains(a.EquipmentId) && a.Status == AssignmentStatus.Active)
                .ToListAsync(cancellationToken);
        var assignmentByEquipmentId = activeAssignments.ToDictionary(a => a.EquipmentId);

        var consultantIds = activeAssignments.Select(a => a.ConsultantId).Distinct().ToList();
        var consultantNames = consultantIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Consultants.AsNoTracking().Where(c => consultantIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);
        ExcelSheetLayout.WriteHeaderRow(sheet, Headers, _ => true);

        var rowIndex = 2;
        foreach (var x in items)
        {
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 1), x.Equipment.Id.ToString());
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 2), x.Equipment.AssetTag);
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 3), x.Type.Name);
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 4), ExcelDisplayValues.Label(x.Type.Kind));
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 5), x.Equipment.Manufacturer);
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 6), x.Equipment.Model);
            // Same safe writer as every other user-controlled field (Phase 8 scope) — it already applies the Text number format
            // this identifier column needs.
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 7), x.Equipment.SerialNumber);
            SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 8), ExcelDisplayValues.Label(x.Equipment.Status));

            if (specsByEquipmentId.TryGetValue(x.Equipment.Id, out var spec))
            {
                SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 9), spec.Processor);
                SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 10), spec.Memory);
                SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 11), spec.Storage);
            }

            // Blank when no Active assignment exists — never inferred from
            // past Handovers (Phase 8 scope).
            if (assignmentByEquipmentId.TryGetValue(x.Equipment.Id, out var assignment))
            {
                SafeExcelCellWriter.WriteText(sheet.Cell(rowIndex, 12), consultantNames.GetValueOrDefault(assignment.ConsultantId));
                var startCell = sheet.Cell(rowIndex, 13);
                startCell.Value = assignment.StartedAtUtc;
                startCell.Style.DateFormat.Format = ExcelSheetLayout.DateFormat;
            }

            rowIndex++;
        }

        // Readable widths and a filter on the header row — this is a list people sort and filter.
        ExcelSheetLayout.FitColumns(sheet, Headers.Length);
        ExcelSheetLayout.AddFilterTable(sheet, TableName, Headers.Length, rowIndex - 1);

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        var bytes = output.ToArray();

        await auditService.RecordAsync(new AuditEntry(
            Domain.Auditing.AuditActions.EquipmentExport.Generated,
            EntityType: "Equipment",
            EntityId: null,
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { RowCount = items.Count }), cancellationToken);

        return bytes;
    }
}
