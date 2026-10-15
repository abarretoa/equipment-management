namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// The Consultant import's columns, in template order. The pt-PT headers
/// follow the labels the application already uses for the same fields
/// (Consultant form, Allocation dialog); each column still accepts the
/// English header of the pre-localization template (ADR-070).
/// </summary>
public static class ConsultantImportColumns
{
    public static readonly ImportColumn FullName = new("FullName", "Nome completo", IsHeaderRequired: true, ["FullName"]);
    public static readonly ImportColumn Nif = new("NIF", "NIF", IsHeaderRequired: true, []);
    public static readonly ImportColumn Email = new("Email", "Email", IsHeaderRequired: true, []);
    public static readonly ImportColumn Phone = new("Phone", "Telefone", IsHeaderRequired: true, ["Phone"]);
    public static readonly ImportColumn Address = new("Address", "Morada", IsHeaderRequired: true, ["Address"]);
    public static readonly ImportColumn WorkforceType = new("WorkforceType", "Regime", IsHeaderRequired: false, ["WorkforceType"]);
    public static readonly ImportColumn EngagementType = new("EngagementType", "Tipo de vínculo", IsHeaderRequired: false, ["EngagementType"]);
    public static readonly ImportColumn RoleOrFunction = new("RoleOrFunction", "Função", IsHeaderRequired: false, ["RoleOrFunction"]);
    public static readonly ImportColumn Company = new("Company", "Empresa / Cliente", IsHeaderRequired: false, ["Company"]);
    public static readonly ImportColumn AllocationStartDate = new("AllocationStartDate", "Data de início", IsHeaderRequired: false, ["AllocationStartDate"]);
    public static readonly ImportColumn AllocationEndDate = new("AllocationEndDate", "Data de fim", IsHeaderRequired: false, ["AllocationEndDate"]);

    public static readonly IReadOnlyList<ImportColumn> All =
    [
        FullName, Nif, Email, Phone, Address, WorkforceType, EngagementType, RoleOrFunction, Company, AllocationStartDate, AllocationEndDate,
    ];

    /// <summary>The pt-PT header for an internal field key — what a message about that field must call it.</summary>
    public static string HeaderFor(string field) => All.FirstOrDefault(c => c.Field == field)?.Header ?? field;
}
