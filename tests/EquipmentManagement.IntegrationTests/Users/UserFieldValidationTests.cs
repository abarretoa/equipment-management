using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentManagement.Api.Contracts.Users;
using EquipmentManagement.Infrastructure.Identity;
using EquipmentManagement.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentManagement.IntegrationTests.Users;

/// <summary>
/// Address length and optional-phone rules on Administration create/update
/// (<c>POST/PUT /api/users</c>), with the HR address rule unchanged. Profile
/// (<c>PUT /api/profile</c>) shares the same rules — see ProfileTests.
/// </summary>
[Collection(Phase1CollectionDefinition.Name)]
public sealed class UserFieldValidationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Address500 = new('a', 500);
    private static readonly string Address501 = new('a', 501);

    // --- Address ---------------------------------------------------------

    [Fact]
    public async Task Create_WithA500CharacterAddress_Succeeds_And501IsA400()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var ok = await client.PostWithCsrfAsync("/api/users", NewUser("HR", address: Address500), csrfToken);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var tooLongEmail = NewEmail();
        var tooLong = await client.PostWithCsrfAsync("/api/users", NewUser("HR", address: Address501, email: tooLongEmail), csrfToken);
        await AssertFieldErrorAsync(tooLong, "Address", UserValidationMessages.AddressTooLong);
        Assert.Null(await FindUserAsync(tooLongEmail));
    }

    [Fact]
    public async Task Update_WithA500CharacterAddress_Succeeds_And501IsA400_LeavingTheUserUnchanged()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (targetEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Original 1, Lisboa");
        var targetId = await ApiTestUsers.GetIdAsync(factory, targetEmail);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var ok = await client.PutWithCsrfAsync($"/api/users/{targetId}", Update(address: Address500), csrfToken);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(Address500, (await FindUserAsync(targetEmail))!.Address);

        var tooLong = await client.PutWithCsrfAsync($"/api/users/{targetId}", Update(address: Address501), csrfToken);
        await AssertFieldErrorAsync(tooLong, "Address", UserValidationMessages.AddressTooLong);
        Assert.Equal(Address500, (await FindUserAsync(targetEmail))!.Address);
    }

    [Fact]
    public async Task HrBlankAddress_IsRejected_WhileAdministrationBlankAddress_IsAccepted()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var hrCreate = await client.PostWithCsrfAsync("/api/users", NewUser("HR", address: "   "), csrfToken);
        Assert.Equal(HttpStatusCode.BadRequest, hrCreate.StatusCode);
        await AssertTitleAsync(hrCreate, "A morada é obrigatória para utilizadores de RH.");

        var adminCreate = await client.PostWithCsrfAsync("/api/users", NewUser("Administration", address: null), csrfToken);
        Assert.Equal(HttpStatusCode.Created, adminCreate.StatusCode);

        var (hrEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua Original 1, Lisboa");
        var hrUpdate = await client.PutWithCsrfAsync($"/api/users/{await ApiTestUsers.GetIdAsync(factory, hrEmail)}", Update(address: ""), csrfToken);
        Assert.Equal(HttpStatusCode.BadRequest, hrUpdate.StatusCode);
        Assert.Equal("Rua Original 1, Lisboa", (await FindUserAsync(hrEmail))!.Address);

        var (adminEmail, _) = await ApiTestUsers.CreateAsync(factory, "Administration", address: "Rua Antiga 3, Porto");
        var adminUpdate = await client.PutWithCsrfAsync($"/api/users/{await ApiTestUsers.GetIdAsync(factory, adminEmail)}", Update(address: null), csrfToken);
        Assert.Equal(HttpStatusCode.OK, adminUpdate.StatusCode);
        Assert.Null((await FindUserAsync(adminEmail))!.Address);
    }

    // --- Phone -----------------------------------------------------------

    public static TheoryData<string?> NoPhoneValues => new() { null, "", "   " };

    [Theory]
    [MemberData(nameof(NoPhoneValues))]
    public async Task Create_WithoutAPhone_Succeeds_AndStoresNull(string? phone)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var email = NewEmail();

        var response = await client.PostWithCsrfAsync("/api/users", NewUser("HR", phone: phone, email: email), csrfToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null((await FindUserAsync(email))!.PhoneNumber);
    }

    [Fact]
    public async Task Create_WithThePhonePropertyOmitted_Succeeds()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var email = NewEmail();

        var response = await client.PostWithCsrfAsync("/api/users", new
        {
            FirstName = "Sem",
            LastName = "Telefone",
            Email = email,
            Role = "Administration",
        }, csrfToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null((await FindUserAsync(email))!.PhoneNumber);
    }

    [Theory]
    [MemberData(nameof(NoPhoneValues))]
    public async Task Update_ClearsAnExistingPhone_WithoutTouchingAnythingElse(string? phone)
    {
        using var client = await LoginAsNewAdministratorAsync();
        var (targetEmail, _) = await ApiTestUsers.CreateAsync(factory, "HR", address: "Rua do Telefone 4, Braga");
        var before = (await FindUserAsync(targetEmail))!;
        Assert.NotNull(before.PhoneNumber);
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync($"/api/users/{before.Id}", new
        {
            FirstName = before.FirstName,
            LastName = before.LastName,
            Phone = phone,
            Address = before.Address,
        }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>(HttpClientTestExtensions.JsonOptions);
        Assert.Null(body!.PhoneNumber);

        var after = (await FindUserAsync(targetEmail))!;
        Assert.Null(after.PhoneNumber);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.Address, after.Address);
        Assert.Equal(before.IsActive, after.IsActive);
        Assert.Equal(before.MustChangePassword, after.MustChangePassword);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal("HR", await RoleOfAsync(targetEmail));
    }

    [Fact]
    public async Task ValidPhone_IsAccepted_AndAnInvalidNonEmptyPhone_IsRejectedOnCreateAndUpdate()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var email = NewEmail();

        var valid = await client.PostWithCsrfAsync("/api/users", NewUser("HR", phone: "+351 912 345 678", email: email), csrfToken);
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        Assert.Equal("+351 912 345 678", (await FindUserAsync(email))!.PhoneNumber);

        var invalidCreate = await client.PostWithCsrfAsync("/api/users", NewUser("HR", phone: "not-a-phone"), csrfToken);
        await AssertFieldErrorAsync(invalidCreate, "Phone", "Indique um número de telefone válido.");

        var target = (await FindUserAsync(email))!;
        var invalidUpdate = await client.PutWithCsrfAsync($"/api/users/{target.Id}", Update(phone: "abc", address: target.Address), csrfToken);
        await AssertFieldErrorAsync(invalidUpdate, "Phone", "Indique um número de telefone válido.");
        Assert.Equal("+351 912 345 678", (await FindUserAsync(email))!.PhoneNumber);
    }

    // --- pt-PT application errors ----------------------------------------

    [Fact]
    public async Task UpdatingAnUnknownUser_Returns404WithAPortugueseMessage()
    {
        using var client = await LoginAsNewAdministratorAsync();
        var csrfToken = await client.GetAntiforgeryTokenAsync();

        var response = await client.PutWithCsrfAsync($"/api/users/{Guid.NewGuid()}", Update(address: null), csrfToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertTitleAsync(response, "Utilizador não encontrado.");
    }

    // --- helpers -----------------------------------------------------------

    private static string NewEmail() => $"hardening.{Guid.NewGuid():N}@example.invalid";

    private static object NewUser(string role, string? address = "Rua Sintética 9, Lisboa", string? phone = "+351911000000", string? email = null) => new
    {
        FirstName = "Utilizador",
        LastName = "Sintético",
        Email = email ?? NewEmail(),
        Phone = phone,
        Role = role,
        Address = address,
    };

    private static object Update(string? address, string? phone = "+351900000000") => new
    {
        FirstName = "Test",
        LastName = "Atualizado",
        Phone = phone,
        Address = address,
    };

    private async Task<HttpClient> LoginAsNewAdministratorAsync()
    {
        var (email, password) = await ApiTestUsers.CreateAsync(factory, "Administration");
        var client = factory.CreateClient();
        await client.LoginAsync(email, password);
        return client;
    }

    private async Task<ApplicationUser?> FindUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    private async Task<string> RoleOfAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await userManager.GetRolesAsync((await userManager.FindByEmailAsync(email))!)).Single();
    }

    private static async Task AssertFieldErrorAsync(HttpResponseMessage response, string field, string expectedMessage)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var messages = problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(expectedMessage, messages);
    }

    private static async Task AssertTitleAsync(HttpResponseMessage response, string expected)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, problem.GetProperty("title").GetString());
    }
}
