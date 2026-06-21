using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using App.Shared.RCL.Models;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class BoardApiIsolationTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task BoardGet_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/board/");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_Login_CreateTodo_OtherUser_CannotToggle()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var suffix = Guid.NewGuid().ToString("N");
        var emailA = $"user-a-{suffix}@integration.test";
        var emailB = $"user-b-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(emailA, password))).IsSuccessStatusCode.Should().BeTrue();
        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(emailB, password))).IsSuccessStatusCode.Should().BeTrue();

        var loginA = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(emailA, password, RememberMe: false));
        loginA.EnsureSuccessStatusCode();
        var tokenA = (await loginA.Content.ReadFromJsonAsync<LoginResponse>(s_json))!.AccessToken;

        var loginB = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(emailB, password, RememberMe: false));
        loginB.EnsureSuccessStatusCode();
        var tokenB = (await loginB.Content.ReadFromJsonAsync<LoginResponse>(s_json))!.AccessToken;

        using var requestCreate = new HttpRequestMessage(HttpMethod.Post, "/api/board/Todo");
        requestCreate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        requestCreate.Content = JsonContent.Create(new ItemTitleRequest("Secret todo A"));
        var createRes = await client.SendAsync(requestCreate);
        createRes.EnsureSuccessStatusCode();
        var created = await createRes.Content.ReadFromJsonAsync<BoardItem>(s_json);
        created.Should().NotBeNull();

        using var requestBList = new HttpRequestMessage(HttpMethod.Get, "/api/board/");
        requestBList.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var listB = await client.SendAsync(requestBList);
        listB.EnsureSuccessStatusCode();
        var snapshotB = await listB.Content.ReadFromJsonAsync<BoardSnapshot>(s_json);
        snapshotB.Should().NotBeNull();
        snapshotB!.Todos.Should().NotContain(t => t.Id == created!.Id);

        using var requestToggle = new HttpRequestMessage(HttpMethod.Post, $"/api/board/Todo/{created!.Id}/toggle");
        requestToggle.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var toggleRes = await client.SendAsync(requestToggle);
        toggleRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateTodo_WithZalgoTitle_ReturnsSanitizedTodo()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var suffix = Guid.NewGuid().ToString("N");
        var email = $"zalgo-user-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, password))).IsSuccessStatusCode.Should().BeTrue();

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(s_json))!.AccessToken;

        // Heavy Zalgo title
        const string zalgoTitle = "k\u0300\u0301\u0302\u0303\u0304a\u0300\u0301\u0302\u0303\u0304r\u0300\u0301\u0302\u0303\u0304o\u0300\u0301\u0302\u0303\u0304l\u0300\u0301\u0302\u0303\u0304y\u0300\u0301\u0302\u0303\u0304";

        using var requestCreate = new HttpRequestMessage(HttpMethod.Post, "/api/board/Todo");
        requestCreate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        requestCreate.Content = JsonContent.Create(new ItemTitleRequest(zalgoTitle));
        var createRes = await client.SendAsync(requestCreate);
        createRes.EnsureSuccessStatusCode();
        var created = await createRes.Content.ReadFromJsonAsync<BoardItem>(s_json);

        created.Should().NotBeNull();
        created!.Title.Should().Be("karoly");
    }
}

[CollectionDefinition(nameof(IntegrationCollection))]
public sealed class IntegrationCollection : ICollectionFixture<PostgresWebAppFactory>;
