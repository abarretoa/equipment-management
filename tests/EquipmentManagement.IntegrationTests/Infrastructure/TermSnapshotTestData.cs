using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>Reads a Responsibility Term version's own frozen snapshot straight from the database (it has no API).</summary>
public static class TermSnapshotTestData
{
    public static async Task<(ResponsibilityTerm Term, List<ResponsibilityTermItem> Items)> GetAsync(ApiWebApplicationFactory factory, Guid termId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var term = await dbContext.ResponsibilityTerms.AsNoTracking().SingleAsync(t => t.Id == termId);
        var items = await dbContext.ResponsibilityTermItems.AsNoTracking()
            .Where(i => i.ResponsibilityTermId == termId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync();
        return (term, items);
    }

    public static async Task<Guid?> GetPhotoSetIdAsync(ApiWebApplicationFactory factory, Guid termId, Guid equipmentId)
    {
        var (_, items) = await GetAsync(factory, termId);
        return items.Single(i => i.EquipmentId == equipmentId).EquipmentPhotoSetId;
    }
}
