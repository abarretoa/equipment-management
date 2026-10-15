using EquipmentManagement.Infrastructure.ClientProfiles;

namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// The Current Inventory import's columns, in template order: the equipment
/// block, then the person/allocation block, then the custody date. Headers
/// reuse exactly the pt-PT labels of the Equipment and Consultant templates
/// for the same fields (ADR-070). This workbook never had an English version,
/// so no column has legacy aliases. Every header must be present in an
/// uploaded file (only cells may be blank) — a missing column would otherwise
/// make a person's data silently disappear and turn an assigned row into stock.
/// </summary>
public static class CurrentInventoryImportColumns
{
    // Equipment
    public static readonly ImportColumn EquipmentType = new("EquipmentType", "Tipo de equipamento", IsHeaderRequired: true, []);
    public static readonly ImportColumn Manufacturer = new("Manufacturer", "Fabricante", IsHeaderRequired: true, []);
    public static readonly ImportColumn Model = new("Model", "Modelo", IsHeaderRequired: true, []);
    public static readonly ImportColumn SerialNumber = new("SerialNumber", "Número de série", IsHeaderRequired: true, []);
    public static readonly ImportColumn Notes = new("Notes", "Observações", IsHeaderRequired: true, []);
    public static readonly ImportColumn Processor = new("Processor", "Processador", IsHeaderRequired: true, []);
    public static readonly ImportColumn Memory = new("Memory", "Memória RAM", IsHeaderRequired: true, []);
    public static readonly ImportColumn Storage = new("Storage", "Armazenamento", IsHeaderRequired: true, []);
    public static readonly ImportColumn Imei = new("Imei", "IMEI", IsHeaderRequired: true, []);
    /// <summary>The header names the company's mobile operator when its profile has one ("N.º de telemóvel / Cartão &lt;operator&gt;").</summary>
    public static readonly ImportColumn MobileNumber = new(
        "MobileNumber", ResponsibilityTermText.MobileNumberImportHeader(ClientProfileLoader.Embedded.Identity), IsHeaderRequired: true, []);
    public static readonly ImportColumn SimSerialNumber = new("SimSerialNumber", "N.º do cartão SIM", IsHeaderRequired: true, []);

    // Consultant + current allocation
    public static readonly ImportColumn FullName = new("FullName", "Nome completo", IsHeaderRequired: true, []);
    public static readonly ImportColumn Nif = new("NIF", "NIF", IsHeaderRequired: true, []);
    public static readonly ImportColumn Email = new("Email", "Email", IsHeaderRequired: true, []);
    public static readonly ImportColumn Phone = new("Phone", "Telefone", IsHeaderRequired: true, []);
    public static readonly ImportColumn Address = new("Address", "Morada", IsHeaderRequired: true, []);
    public static readonly ImportColumn WorkforceType = new("WorkforceType", "Regime", IsHeaderRequired: true, []);
    public static readonly ImportColumn EngagementType = new("EngagementType", "Tipo de vínculo", IsHeaderRequired: true, []);
    public static readonly ImportColumn RoleOrFunction = new("RoleOrFunction", "Função", IsHeaderRequired: true, []);
    public static readonly ImportColumn Company = new("Company", "Empresa / Cliente", IsHeaderRequired: true, []);
    public static readonly ImportColumn AllocationStartDate = new("AllocationStartDate", "Data de início", IsHeaderRequired: true, []);
    public static readonly ImportColumn AllocationEndDate = new("AllocationEndDate", "Data de fim", IsHeaderRequired: true, []);

    // Current custody
    public static readonly ImportColumn AssignmentDate = new("AssignmentDate", "Data de atribuição", IsHeaderRequired: true, []);

    public static readonly IReadOnlyList<ImportColumn> All =
    [
        EquipmentType, Manufacturer, Model, SerialNumber, Notes, Processor, Memory, Storage, Imei, MobileNumber, SimSerialNumber,
        FullName, Nif, Email, Phone, Address, WorkforceType, EngagementType, RoleOrFunction, Company, AllocationStartDate, AllocationEndDate,
        AssignmentDate,
    ];

    /// <summary>Every column of the person/allocation/custody block — any one of them filled in makes the row an assigned row.</summary>
    public static readonly IReadOnlyList<ImportColumn> ConsultantBlock =
    [
        FullName, Nif, Email, Phone, Address, WorkforceType, EngagementType, RoleOrFunction, Company, AllocationStartDate, AllocationEndDate,
        AssignmentDate,
    ];

    /// <summary>
    /// The persisted column length of each free-text field (ApplicationDbContext),
    /// so an over-long value is a preview error rather than a failure at
    /// confirmation. Anything not listed is bounded by the generic cell limit.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> MaxLengths = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [Manufacturer.Field] = 200,
        [Model.Field] = 200,
        [SerialNumber.Field] = 200,
        [Notes.Field] = 2000,
        [Processor.Field] = 200,
        [Memory.Field] = 100,
        [Storage.Field] = 200,
        [Imei.Field] = 32,
        [MobileNumber.Field] = 30,
        [SimSerialNumber.Field] = 30,
        [FullName.Field] = 300,
        [Email.Field] = 320,
        [Phone.Field] = 50,
        [Address.Field] = 500,
        [RoleOrFunction.Field] = 200,
    };

    /// <summary>The pt-PT header for an internal field key — what a message about that field must call it.</summary>
    public static string HeaderFor(string field) => All.FirstOrDefault(c => c.Field == field)?.Header ?? field;
}
