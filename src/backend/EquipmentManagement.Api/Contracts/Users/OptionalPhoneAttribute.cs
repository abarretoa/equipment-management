using System.ComponentModel.DataAnnotations;

namespace EquipmentManagement.Api.Contracts.Users;

/// <summary>
/// Phone validation for the optional internal-user phone, shared by the
/// Administration user requests and <c>UpdateProfileRequest</c>. <c>null</c>,
/// empty and whitespace-only mean "no phone" and are valid (the service stores
/// them as <c>null</c> — <c>Domain.Users.UserProfileRules.NormalizeOptionalPhone</c>);
/// any other value must pass the framework's <see cref="PhoneAttribute"/> check.
/// The plain <c>[Phone]</c> attribute rejects an empty string, which made a phone
/// impossible to clear.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class OptionalPhoneAttribute : ValidationAttribute
{
    private static readonly PhoneAttribute Phone = new();

    public OptionalPhoneAttribute()
        : base("Indique um número de telefone válido.")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value is string text && (string.IsNullOrWhiteSpace(text) || Phone.IsValid(text)));
}
