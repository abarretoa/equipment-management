using System.Runtime.CompilerServices;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.ResponsibilityTerms;

/// <summary>
/// Issues a new, globally unique <see cref="ResponsibilityTerm.DocumentNumber"/>
/// from the database-backed sequence declared in <c>ApplicationDbContext</c> —
/// the exact same pattern, for the exact same reason, as
/// <c>Infrastructure.Equipments.AssetTagGenerator</c> (ADR-026): never
/// <c>SELECT MAX(...)+1</c>, since Postgres's <c>nextval()</c> is atomic and
/// safe under concurrent callers and a numbering gap is harmless while a
/// duplicate document number is not.
/// </summary>
public sealed class DocumentNumberGenerator(ApplicationDbContext dbContext, TimeProvider timeProvider)
{
    private static readonly string NextValueSql =
        $"SELECT nextval('{ApplicationDbContext.ResponsibilityTermDocumentNumberSequenceName}') AS \"Value\"";

    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var sequenceValue = await dbContext.Database
            .SqlQuery<long>(FormattableStringFactory.Create(NextValueSql))
            .SingleAsync(cancellationToken);

        var year = timeProvider.GetUtcNow().UtcDateTime.Year;
        return DocumentNumberFormatter.Format(year, sequenceValue);
    }
}
