using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EquipmentManagement.Domain.Users;

/// <summary>
/// A deterministic SHA-256 fingerprint of a role→permissions map (ADR-078).
/// Issued into every auth cookie and compared on every request, so a deployment
/// that changes <see cref="RolePermissions"/> invalidates cookies carrying the
/// old map's permission claims. Not a secret.
/// </summary>
/// <remarks>
/// Canonical form: roles sorted ordinally; each role's permissions
/// de-duplicated and sorted ordinally; serialized as a JSON array of
/// <c>[role, [permissions…]]</c> pairs (JSON quoting makes the encoding
/// unambiguous whatever characters an identifier contains), UTF-8, hashed,
/// lower-case hex. Insertion order never matters; adding, removing or moving a
/// permission between roles always changes the result.
/// </remarks>
public static class PermissionMapFingerprint
{
    public static string Compute(IReadOnlyDictionary<string, IEnumerable<string>> map)
    {
        var canonical = map
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new object[] { entry.Key, entry.Value.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() })
            .ToArray();

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))));
    }
}
