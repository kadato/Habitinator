using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using App.Shared.RCL.Models;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class BoardPagingIntegrationTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Sync_limit_pages_union_equals_full_set()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = await RegisterAndLoginAsync(client);
        var baseline = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O");

        var created = new List<BoardItem>();
        for (var i = 0; i < 3; i++)
        {
            created.Add(await CreateTodoAsync(client, token, $"Paged {i}"));
        }

        var page1 = await GetSyncAsync(client, token, baseline, limit: 2);
        page1.Items.Should().HaveCount(2);
        page1.NextCursor.Should().Contain("|");

        // Registration seeds a starter board, so page through until the server
        // reports a complete page and check the union covers the created rows.
        var seen = new HashSet<Guid>(page1.Items.Select(i => i.Item.Id));
        foreach (var d in page1.DeletedItemIds)
        {
            seen.Add(d);
        }

        var cursorNow = page1.NextCursor;
        string? lastCursor = null;
        for (var i = 0; i < 10; i++)
        {
            var page = await GetSyncAsync(client, token, cursorNow, limit: 2);
            foreach (var u in page.Items)
            {
                seen.Add(u.Item.Id);
            }

            foreach (var d in page.DeletedItemIds)
            {
                seen.Add(d);
            }

            lastCursor = page.NextCursor;
            if (!page.NextCursor.Contains('|'))
            {
                break;
            }

            cursorNow = page.NextCursor;
        }

        seen.Should().Contain(created.Select(c => c.Id));
        lastCursor.Should().NotBeNullOrEmpty();
        lastCursor.Should().NotContain("|");
    }

    [Fact]
    public async Task Sync_invalid_limit_returns_400()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = await RegisterAndLoginAsync(client);
        var cursor = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"));

        foreach (var bad in new[] { "0", "-1", "99999", "abc" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/board/sync?cursor={cursor}&limit={bad}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Snapshot_limit_caps_rows_and_rejects_invalid()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = await RegisterAndLoginAsync(client);
        await CreateTodoAsync(client, token, "One");
        await CreateTodoAsync(client, token, "Two");
        await CreateTodoAsync(client, token, "Three");

        using var capped = new HttpRequestMessage(HttpMethod.Get, "/api/board?limit=2");
        capped.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var cappedRes = await client.SendAsync(capped);
        cappedRes.EnsureSuccessStatusCode();
        var cappedSnapshot = (await cappedRes.Content.ReadFromJsonAsync<BoardSnapshot>(s_json))!;
        (cappedSnapshot.Habits.Count + cappedSnapshot.Dailies.Count + cappedSnapshot.Todos.Count).Should().Be(2);

        using var bad = new HttpRequestMessage(HttpMethod.Get, "/api/board?limit=0");
        bad.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var badRes = await client.SendAsync(bad);
        badRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<BoardSyncDelta> GetSyncAsync(HttpClient client, string token, string cursor, int limit)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/board/sync?cursor={Uri.EscapeDataString(cursor)}&limit={limit}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<BoardSyncDelta>(s_json))!;
    }

    private static async Task<BoardItem> CreateTodoAsync(HttpClient client, string token, string title)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/board/Todo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new ItemTitleRequest(title), options: s_json);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<BoardItem>(s_json))!;
    }

    private static async Task<string> RegisterAndLoginAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"page-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var reg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        reg.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>(s_json))!.AccessToken;
    }
}
