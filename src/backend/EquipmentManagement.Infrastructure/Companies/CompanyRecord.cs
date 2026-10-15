namespace EquipmentManagement.Infrastructure.Companies;

/// <summary>The safe, service-layer projection of a company — never the EF entity itself.</summary>
public sealed record CompanyRecord(Guid Id, string Name, bool IsActive, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record CreateCompanyData(string Name);

public sealed record UpdateCompanyData(string Name);

public sealed record CompanyListFilter(string? Search, bool? IsActive);
