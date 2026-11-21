using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Web.Data;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Web.IntegrationTests;

/// <summary>
/// A signed-in identity can outlive its database row, for example after a database reset
/// or when the account was deleted. Those requests must fail as unauthorized, never as a
/// settings 404 or a foreign key 500.
/// </summary>
[Collection(nameof(IntegrationCollection))]
public sealed class StaleIdentityTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };
    private const string Password = "TestUser1!Aa";

    [Fact]
    public async Task SettingsRead_WithJwtForDeletedUser_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var email = await RegisterAsync(client);
        var token = await LoginAsync(client, email);

        await DeleteUserAsync(email);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings/preferences");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BoardMutation_WithCookieForDeletedUser_ReturnsUnauthorizedAndClearsCookie()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = await RegisterAsync(client);

        using var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = Password,
            ["RememberMe"] = "false",
        });
        (await client.PostAsync("/api/auth/cookie-login", loginForm))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/board/Daily");
        create.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        create.Content = JsonContent.Create(new ItemTitleRequest("Stale cookie item"));
        var created = await (await client.SendAsync(create)).Content.ReadFromJsonAsync<BoardItem>(s_json);

        await DeleteUserAsync(email);

        using var update = new HttpRequestMessage(HttpMethod.Put, $"/api/board/dailies/{created!.Id}");
        update.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        update.Content = JsonContent.Create(new { title = "Edited", repeat = 0, repeatInterval = 1 });
        var response = await client.SendAsync(update);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var status = await client.GetFromJsonAsync<AuthStatusDto>("/api/auth/status", s_json);
        status.Should().NotBeNull();
        status.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task CookieLogin_WithStaleCookie_CanSignInAsAnotherUser()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var staleEmail = await RegisterAsync(client);

        using var staleForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = staleEmail,
            ["Password"] = Password,
            ["RememberMe"] = "false",
        });
        (await client.PostAsync("/api/auth/cookie-login", staleForm))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);

        await DeleteUserAsync(staleEmail);

        var freshEmail = await RegisterAsync(client);
        using var freshForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = freshEmail,
            ["Password"] = Password,
            ["RememberMe"] = "false",
        });
        (await client.PostAsync("/api/auth/cookie-login", freshForm))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);

        var status = await client.GetFromJsonAsync<AuthStatusDto>("/api/auth/status", s_json);
        status.Should().NotBeNull();
        status.IsAuthenticated.Should().BeTrue();
        status.Email.Should().Be(freshEmail);
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var email = $"stale-{Guid.NewGuid():N}@integration.test";
        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));
        register.IsSuccessStatusCode.Should().BeTrue();
        return email;
    }

    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>(s_json))!.AccessToken;
    }

    private async Task DeleteUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Users.Where(u => u.Email == email).ExecuteDeleteAsync();
    }
}
