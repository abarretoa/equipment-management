using System.ComponentModel.DataAnnotations;

namespace EquipmentManagement.Api.Contracts.Companies;

public sealed record CreateCompanyRequest([Required, MaxLength(200)] string Name);

public sealed record UpdateCompanyRequest([Required, MaxLength(200)] string Name);

public sealed record CompanyResponse(Guid Id, string Name, bool IsActive, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
