using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using App.Shared.RCL.Models;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class BoardItemEndpointTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task GetItem_ReturnsOnlyThatItem()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = await RegisterAndLoginAsync(client);

        var created = await CreateTodoAsync(client, token, "Call the dentist");
        var other = await CreateTodoAsync(client, token, "Buy milk");

        var item = await GetItemAsync(client, token, created.Id);

        item.Should().NotBeNull();
        item.Id.Should().Be(created.Id);
        item.Title.Should().Be("Call the dentist");
        other.Id.Should().NotBe(created.Id);
    }

    [Fact]
    public async Task GetItem_OtherUsersItem_ReturnsNotFound()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var ownerToken = await RegisterAndLoginAsync(client);
        var otherToken = await RegisterAndLoginAsync(client);
        var created = await CreateTodoAsync(client, ownerToken, "Private todo");

        var res = await SendAuthorizedAsync(client, otherToken, $"/api/board/items/{created.Id}");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetItem_ArchivedItem_ReturnsNotFound()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = await RegisterAndLoginAsync(client);
        var created = await CreateTodoAsync(client, token, "Old todo");

        using var archive = new HttpRequestMessage(HttpMethod.Post, $"/api/board/Todo/{created.Id}/archive");
        archive.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(archive)).EnsureSuccessStatusCode();

        var res = await SendAuthorizedAsync(client, token, $"/api/board/items/{created.Id}");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetItem_MissingItem_ReturnsNotFound()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = await RegisterAndLoginAsync(client);

        var res = await SendAuthorizedAsync(client, token, $"/api/board/items/{Guid.NewGuid()}");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<BoardItem> GetItemAsync(HttpClient client, string token, Guid itemId)
    {
        var res = await SendAuthorizedAsync(client, token, $"/api/board/items/{itemId}");
        res.EnsureSuccessStatusCode();
        var item = await res.Content.ReadFromJsonAsync<BoardItem>(s_json);
        item.Should().NotBeNull();
        return item;
    }

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<BoardItem> CreateTodoAsync(HttpClient client, string token, string title)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/board/Todo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new ItemTitleRequest(title));
        var res = await client.SendAsync(request);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<BoardItem>(s_json))!;
    }

    private static async Task<string> RegisterAndLoginAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"item-endpoint-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password)))
            .EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>(s_json))!.AccessToken;
    }
}
