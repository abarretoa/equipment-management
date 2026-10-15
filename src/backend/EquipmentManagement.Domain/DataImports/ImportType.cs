namespace EquipmentManagement.Domain.DataImports;

/// <summary>
/// The independent Excel import workflows: the two Phase 8 imports (Phase 8 scope) and the Current Inventory import (Consultants,
/// Allocations, Equipment and their current custody from one workbook).
/// Deliberately not a generic "entity name" — a closed, small vocabulary
/// keeps the importer explicit rather than a reflection-driven framework.
/// </summary>
public enum ImportType
{
    Consultants,
    Equipment,
    CurrentInventory,
}
