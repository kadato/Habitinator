using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using App.Shared.RCL.Models;

using FluentAssertions;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class DeleteAccountIntegrationTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task DeleteAccount_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest("secret"));

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteAccount_WithWrongPassword_KeepsTheAccount()
    {
        var (client, email, password) = await RegisterAndSignInAsync();

        var deleteRes = await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest("wrong-password"));
        deleteRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await deleteRes.Content.ReadAsStringAsync();
        body.Should().Contain("Current password is incorrect.");

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteAccount_WithTheCurrentPassword_RemovesTheAccount()
    {
        var (client, email, password) = await RegisterAndSignInAsync();

        var deleteRes = await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(password));
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteAccount_WithAnEmptyPassword_IsRejectedByValidation()
    {
        var (client, email, password) = await RegisterAndSignInAsync();

        var deleteRes = await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(string.Empty));
        deleteRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<(HttpClient Client, string Email, string Password)> RegisterAndSignInAsync()
    {
        var client = factory.CreateClient();
        var email = $"delete-account-{Guid.NewGuid():N}@integration.test";
        const string password = "TestUser1!Aa";

        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();

        var loginData = await login.Content.ReadFromJsonAsync<LoginResponse>(s_json);
        loginData.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(loginData);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginData.AccessToken);
        return (client, email, password);
    }
}
