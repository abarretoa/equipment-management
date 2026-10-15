using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentManagement.Api.Contracts.Auth;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Auth;

/// <summary>
/// <c>POST /api/auth/change-password</c>: the server's password policy (minimum
/// 12 characters, with a digit, an upper- and a lower-case letter) and its
/// pt-PT messages, for both the voluntary and the forced first-login change.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class ChangePasswordTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string ValidTwelveCharacters = "Abcdefgh1234";

    [Fact]
    public async Task ElevenCharacters_IsRejected_WithAPortugueseMessage_AndThePasswordIsUnchanged()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/auth/change-password",
            new ChangePasswordRequest(password, "Abcdefgh123"), csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertOnlyFieldErrorAsync(response, "NewPassword", "A palavra-passe deve ter pelo menos 12 caracteres.");
        Assert.True(await PasswordIsAsync(email, password));
    }

    [Fact]
    public async Task TwelveCharacters_MeetingTheOtherRules_IsAccepted_AndBecomesTheLoginPassword()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua da Palavra 1, Lisboa");
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/auth/change-password",
            new ChangePasswordRequest(password, ValidTwelveCharacters), csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await PasswordIsAsync(email, ValidTwelveCharacters));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("abcdefgh1234", "A palavra-passe deve ter pelo menos uma letra maiúscula (A-Z).")]
    [InlineData("ABCDEFGH1234", "A palavra-passe deve ter pelo menos uma letra minúscula (a-z).")]
    [InlineData("Abcdefghijkl", "A palavra-passe deve ter pelo menos um algarismo (0-9).")]
    public async Task TheOtherRulesStillApply(string newPassword, string expectedMessage)
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/auth/change-password",
            new ChangePasswordRequest(password, newPassword), csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertOnlyFieldErrorAsync(response, "NewPassword", expectedMessage);
    }

    [Fact]
    public async Task WrongCurrentPassword_IsRejected_WithAPortugueseMessage()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PostWithCsrfAsync("/api/auth/change-password",
            new ChangePasswordRequest("Wrong-Current-1", ValidTwelveCharacters), csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertOnlyFieldErrorAsync(response, "CurrentPassword", "A palavra-passe atual está incorreta.");
        Assert.True(await PasswordIsAsync(email, password));
    }

    [Fact]
    public async Task ForcedFirstLoginChange_StillUnblocksTheAccount()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Forçada 2, Porto");
        await SetMustChangePasswordAsync(email);
        using var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var blocked = await client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        var blockedProblem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("É necessário alterar a palavra-passe.", blockedProblem.GetProperty("title").GetString());
        Assert.Equal(
            "Esta conta tem uma palavra-passe temporária que tem de ser alterada antes de continuar.",
            blockedProblem.GetProperty("detail").GetString());

        var tooShort = await client.PostWithCsrfAsync("/api/auth/change-password",
            new ChangePasswordRequest(password, "Abcdefgh123"), csrfToken);
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/profile")).StatusCode);

        var response = await client.PostWithCsrfAsync("/api/auth/change-password",
            new ChangePasswordRequest(password, ValidTwelveCharacters), csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", HttpClientTestExtensions.JsonOptions);
        Assert.False(me!.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/profile")).StatusCode);
    }

    /// <summary>
    /// The error is keyed by the field it concerns ("CurrentPassword" or
    /// "NewPassword"), so the UI can show it under that input — and appears
    /// under no other key (in particular not the unattributed "" key).
    /// </summary>
    private static async Task AssertOnlyFieldErrorAsync(HttpResponseMessage response, string expectedField, string expectedMessage)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = problem.GetProperty("errors").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(e => e.GetString()!).ToArray());

        Assert.Equal([expectedField], errors.Keys);
        Assert.Contains(expectedMessage, errors[expectedField]);
    }

    private async Task<bool> PasswordIsAsync(string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.CheckPasswordAsync((await userManager.FindByEmailAsync(email))!, password);
    }

    private async Task SetMustChangePasswordAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByEmailAsync(email))!;
        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);
    }
}
