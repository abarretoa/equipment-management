namespace EquipmentManagement.Api.Contracts;

/// <summary>The wire shape for every server-side-paginated list endpoint (docs/PROJECT_SPEC.md, section 24).</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
