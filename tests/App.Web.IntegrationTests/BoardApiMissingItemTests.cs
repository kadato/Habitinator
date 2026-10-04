using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using App.Shared.RCL.Models;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class BoardApiMissingItemTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Put_MissingHabit_Returns_NotFound()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/board/habits/{Guid.NewGuid()}");
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        put.Content = JsonContent.Create(new HabitUpdateRequest(
            "Ghost", null, null, true, true, HabitResetPeriod.Daily, 0, 0, null, 2.5), options: s_json);
        var res = await client.SendAsync(put);

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_MissingTodo_Returns_NotFound()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/board/todos/{Guid.NewGuid()}");
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        put.Content = JsonContent.Create(new TodoUpdateRequest("Ghost", null, null, null, null, 1.0), options: s_json);
        var res = await client.SendAsync(put);

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<(string Token, string Email)> RegisterAndLoginAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"missing-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var reg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        reg.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(s_json))!.AccessToken;
        return (token, email);
    }
}
