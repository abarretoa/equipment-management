namespace EquipmentManagement.Infrastructure.Common;

/// <summary>
/// A server-side-paginated result (docs/PROJECT_SPEC.md, section 24: "listagens
/// devem usar paginação server-side"). Shared between the user-management and
/// audit list endpoints rather than duplicated per feature.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
