using EquipmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace EquipmentManagement.UnitTests.Infrastructure.Identity;

public class PortugueseIdentityErrorDescriberTests
{
    private static readonly IdentityErrorDescriber Default = new();
    private static readonly PortugueseIdentityErrorDescriber Portuguese = new();

    public static TheoryData<string, Func<IdentityErrorDescriber, IdentityError>> Errors => new()
    {
        { "PasswordTooShort", d => d.PasswordTooShort(12) },
        { "PasswordMismatch", d => d.PasswordMismatch() },
        { "PasswordRequiresDigit", d => d.PasswordRequiresDigit() },
        { "PasswordRequiresLower", d => d.PasswordRequiresLower() },
        { "PasswordRequiresUpper", d => d.PasswordRequiresUpper() },
        { "PasswordRequiresNonAlphanumeric", d => d.PasswordRequiresNonAlphanumeric() },
        { "PasswordRequiresUniqueChars", d => d.PasswordRequiresUniqueChars(2) },
        { "DuplicateEmail", d => d.DuplicateEmail("x@example.invalid") },
        { "DuplicateUserName", d => d.DuplicateUserName("x@example.invalid") },
        { "InvalidEmail", d => d.InvalidEmail("x") },
        { "InvalidUserName", d => d.InvalidUserName("x") },
        { "ConcurrencyFailure", d => d.ConcurrencyFailure() },
        { "InvalidToken", d => d.InvalidToken() },
        { "DefaultError", d => d.DefaultError() },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void KeepsIdentitysErrorCode_ButReplacesTheEnglishDescription(string code, Func<IdentityErrorDescriber, IdentityError> describe)
    {
        var english = describe(Default);
        var portuguese = describe(Portuguese);

        // Codes are what application code branches on; they must not change.
        Assert.Equal(code, english.Code);
        Assert.Equal(english.Code, portuguese.Code);
        Assert.NotEqual(english.Description, portuguese.Description);
    }

    [Fact]
    public void PasswordTooShort_StatesTheConfiguredMinimum()
    {
        Assert.Equal("A palavra-passe deve ter pelo menos 12 caracteres.", Portuguese.PasswordTooShort(12).Description);
    }
}
