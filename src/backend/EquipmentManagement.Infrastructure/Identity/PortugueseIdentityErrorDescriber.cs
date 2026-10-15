using System.Globalization;
using Microsoft.AspNetCore.Identity;

namespace EquipmentManagement.Infrastructure.Identity;

/// <summary>
/// pt-PT descriptions for the ASP.NET Core Identity errors the user-management
/// and change-password flows can return to the UI (the descriptions reach the
/// user through <c>POST /api/auth/change-password</c> and the
/// <c>/api/users</c> problem responses). Only the descriptions change: error
/// codes stay Identity's own, so code that branches on them
/// (<c>UserManagementService.IsDuplicateFailure</c>/<c>IsConcurrencyFailure</c>)
/// is unaffected. Errors not overridden here keep Identity's default text.
/// </summary>
public sealed class PortugueseIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() =>
        Describe(nameof(DefaultError), "Ocorreu um erro inesperado.");

    public override IdentityError ConcurrencyFailure() =>
        Describe(nameof(ConcurrencyFailure), "O utilizador foi alterado por outra pessoa. Recarregue e tente novamente.");

    public override IdentityError PasswordMismatch() =>
        Describe(nameof(PasswordMismatch), "A palavra-passe atual está incorreta.");

    public override IdentityError InvalidToken() =>
        Describe(nameof(InvalidToken), "O pedido expirou ou é inválido. Tente novamente.");

    public override IdentityError PasswordTooShort(int length) =>
        Describe(nameof(PasswordTooShort), string.Create(CultureInfo.InvariantCulture, $"A palavra-passe deve ter pelo menos {length} caracteres."));

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Describe(nameof(PasswordRequiresUniqueChars), string.Create(CultureInfo.InvariantCulture, $"A palavra-passe deve ter pelo menos {uniqueChars} caracteres diferentes."));

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Describe(nameof(PasswordRequiresNonAlphanumeric), "A palavra-passe deve ter pelo menos um carácter não alfanumérico.");

    public override IdentityError PasswordRequiresDigit() =>
        Describe(nameof(PasswordRequiresDigit), "A palavra-passe deve ter pelo menos um algarismo (0-9).");

    public override IdentityError PasswordRequiresLower() =>
        Describe(nameof(PasswordRequiresLower), "A palavra-passe deve ter pelo menos uma letra minúscula (a-z).");

    public override IdentityError PasswordRequiresUpper() =>
        Describe(nameof(PasswordRequiresUpper), "A palavra-passe deve ter pelo menos uma letra maiúscula (A-Z).");

    public override IdentityError InvalidEmail(string? email) =>
        Describe(nameof(InvalidEmail), "O e-mail indicado não é válido.");

    public override IdentityError DuplicateEmail(string email) =>
        Describe(nameof(DuplicateEmail), "Já existe um utilizador com este e-mail.");

    public override IdentityError InvalidUserName(string? userName) =>
        Describe(nameof(InvalidUserName), "O e-mail indicado não é válido.");

    public override IdentityError DuplicateUserName(string userName) =>
        Describe(nameof(DuplicateUserName), "Já existe um utilizador com este e-mail.");

    private static IdentityError Describe(string code, string description) => new() { Code = code, Description = description };
}
