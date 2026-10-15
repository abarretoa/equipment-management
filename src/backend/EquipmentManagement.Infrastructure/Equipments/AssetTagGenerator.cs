using System.Runtime.CompilerServices;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Equipments;

/// <summary>
/// Issues a new, globally unique <see cref="Equipment.AssetTag"/> from the
/// database-backed sequence declared in <c>ApplicationDbContext</c>. Never
/// <c>SELECT MAX(...)+1</c> — see ADR-026 for why:
/// Postgres's <c>nextval()</c> is atomic and safe under concurrent callers,
/// and its results are never reused even if the calling transaction rolls
/// back, which is exactly the property needed here (a small gap in the
/// sequence is harmless; a duplicate AssetTag is not).
/// </summary>
public sealed class AssetTagGenerator(ApplicationDbContext dbContext)
{
    // Built as a plain string, not an EF interpolated-SQL argument: the
    // sequence name is a compile-time constant (never user input), and
    // nextval() needs it as literal SQL text, not a bound parameter (Postgres
    // cannot reliably infer that a parameter should cast to regclass).
    private static readonly string NextValueSql =
        $"SELECT nextval('{ApplicationDbContext.AssetTagSequenceName}') AS \"Value\"";

    public async Task<string> GenerateAsync(string normalizedPrefix, CancellationToken cancellationToken = default)
    {
        var sequenceValue = await dbContext.Database
            .SqlQuery<long>(FormattableStringFactory.Create(NextValueSql))
            .SingleAsync(cancellationToken);

        return AssetTagFormatter.Format(normalizedPrefix, sequenceValue);
    }
}
