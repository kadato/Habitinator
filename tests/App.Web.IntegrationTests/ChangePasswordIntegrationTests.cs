using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using App.Shared.RCL.Models;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class ChangePasswordIntegrationTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };
    private const string Password = "TestUser1!Aa";

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ReturnsIdentityDetail()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (_, token) = await RegisterAndLoginAsync(client);

        using var res = await ChangeAsync(client, token, "WrongPass1!Aa", "NewPassword1!Aa");

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detail = await ReadDetailAsync(res);
        detail.Should().Contain("Incorrect password");
    }

    [Fact]
    public async Task ChangePassword_WeakNewPassword_IsRejected()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (_, token) = await RegisterAndLoginAsync(client);

        using var res = await ChangeAsync(client, token, Password, "short");

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_ValidChange_AllowsSignInWithNewPassword()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (email, token) = await RegisterAndLoginAsync(client);
        const string newPassword = "NewPassword1!Aa";

        using var res = await ChangeAsync(client, token, Password, newPassword);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, newPassword, RememberMe: false));
        login.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> ChangeAsync(HttpClient client, string token, string current, string next)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/account/change-password");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new ChangePasswordRequest(current, next), options: s_json);
        return await client.SendAsync(req);
    }

    private static async Task<string> ReadDetailAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("detail").GetString() ?? string.Empty;
    }

    private static async Task<(string Email, string Token)> RegisterAndLoginAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"change-{suffix}@integration.test";

        var reg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));
        reg.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(s_json))!.AccessToken;
        return (email, token);
    }
}
