using System.Security.Cryptography;

namespace EquipmentManagement.Infrastructure.Identity;

/// <summary>
/// Generates a cryptographically random temporary password for admin-created
/// users and admin-initiated password resets (Phase 1 scope). Deliberately exceeds the configured
/// password policy's minimum requirements (length, character variety) so it stays
/// valid even if that policy is tightened later — never persisted or logged
/// anywhere; the caller is responsible for returning it exactly once, in the
/// immediate API response, and never storing it in plaintext.
/// </summary>
public static class TemporaryPasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+";
    private const string AllCharacters = Upper + Lower + Digits + Symbols;
    private const int Length = 20;

    public static string Generate()
    {
        Span<char> password = stackalloc char[Length];

        // Guarantee at least one character from each class, then fill the rest
        // randomly, then shuffle so the guaranteed characters aren't predictably
        // positioned.
        password[0] = PickFrom(Upper);
        password[1] = PickFrom(Lower);
        password[2] = PickFrom(Digits);
        password[3] = PickFrom(Symbols);

        for (var i = 4; i < Length; i++)
        {
            password[i] = PickFrom(AllCharacters);
        }

        Shuffle(password);

        return new string(password);
    }

    private static char PickFrom(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static void Shuffle(Span<char> value)
    {
        for (var i = value.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (value[i], value[j]) = (value[j], value[i]);
        }
    }
}
