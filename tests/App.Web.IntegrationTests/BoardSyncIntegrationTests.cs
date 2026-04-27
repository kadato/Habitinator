using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using App.Shared.RCL.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class BoardSyncIntegrationTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Idempotency_same_key_same_body_replays_response_without_double_increment()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/board/Habit");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(new ItemTitleRequest("Water"), options: s_json);
        var createRes = await client.SendAsync(createReq);
        createRes.EnsureSuccessStatusCode();
        var created = (await createRes.Content.ReadFromJsonAsync<BoardItem>(s_json))!;

        var idem = Guid.NewGuid().ToString();
        using var inc1 = IncrementRequest(token, created.Id, idem);
        var r1 = await client.SendAsync(inc1);
        r1.EnsureSuccessStatusCode();
        var after1 = (await r1.Content.ReadFromJsonAsync<BoardItem>(s_json))!;

        using var inc2 = IncrementRequest(token, created.Id, idem);
        var r2 = await client.SendAsync(inc2);
        r2.EnsureSuccessStatusCode();
        var after2 = (await r2.Content.ReadFromJsonAsync<BoardItem>(s_json))!;

        Assert.Equal(after1.Counter, after2.Counter);
        Assert.Equal(after1.ServerUpdatedAtUtc, after2.ServerUpdatedAtUtc);
    }

    [Fact]
    public async Task Idempotency_same_key_different_fingerprint_returns_409()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        var idem = Guid.NewGuid().ToString();
        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/board/Todo");
        first.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        first.Headers.TryAddWithoutValidation("Idempotency-Key", idem);
        first.Content = JsonContent.Create(new ItemTitleRequest("First title"), options: s_json);
        var firstRes = await client.SendAsync(first);
        firstRes.EnsureSuccessStatusCode();

        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/board/Todo");
        second.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        second.Headers.TryAddWithoutValidation("Idempotency-Key", idem);
        second.Content = JsonContent.Create(new ItemTitleRequest("Different title"), options: s_json);
        var secondRes = await client.SendAsync(second);
        Assert.Equal(HttpStatusCode.Conflict, secondRes.StatusCode);
    }

    [Fact]
    public async Task Concurrent_first_idempotent_requests_single_increment()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/board/Habit");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(new ItemTitleRequest("Pushups"), options: s_json);
        var createRes = await client.SendAsync(createReq);
        createRes.EnsureSuccessStatusCode();
        var created = (await createRes.Content.ReadFromJsonAsync<BoardItem>(s_json))!;

        var idem = Guid.NewGuid().ToString();
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => client.SendAsync(IncrementRequest(token, created.Id, idem))));
        try
        {
            foreach (var msg in responses)
                msg.EnsureSuccessStatusCode();
        }
        finally
        {
            foreach (var msg in responses)
                msg.Dispose();
        }

        using var snapReq = new HttpRequestMessage(HttpMethod.Get, "/api/board/");
        snapReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var snapRes = await client.SendAsync(snapReq);
        snapRes.EnsureSuccessStatusCode();
        var snap = (await snapRes.Content.ReadFromJsonAsync<BoardSnapshot>(s_json))!;
        var row = Assert.Single(snap.Habits, h => h.Id == created.Id);
        Assert.Equal(1, row.Counter);
    }

    [Fact]
    public async Task Sync_cursor_returns_tombstone_then_local_snapshot_omits_deleted_id()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/board/Todo");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(new ItemTitleRequest("Temp"), options: s_json);
        var createRes = await client.SendAsync(createReq);
        createRes.EnsureSuccessStatusCode();
        var created = (await createRes.Content.ReadFromJsonAsync<BoardItem>(s_json))!;
        var cursor0 = created.ServerUpdatedAtUtc!.Value.ToString("O");

        using var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/board/Todo/{created.Id}");
        del.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var delRes = await client.SendAsync(del);
        delRes.EnsureSuccessStatusCode();

        using var syncReq = new HttpRequestMessage(HttpMethod.Get, $"/api/board/sync?cursor={Uri.EscapeDataString(cursor0)}");
        syncReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var syncRes = await client.SendAsync(syncReq);
        syncRes.EnsureSuccessStatusCode();
        var delta = (await syncRes.Content.ReadFromJsonAsync<BoardSyncDelta>(s_json))!;
        Assert.Contains(created.Id, delta.DeletedItemIds);

        using var snapReq = new HttpRequestMessage(HttpMethod.Get, "/api/board/");
        snapReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var snapRes = await client.SendAsync(snapReq);
        snapRes.EnsureSuccessStatusCode();
        var snap = (await snapRes.Content.ReadFromJsonAsync<BoardSnapshot>(s_json))!;
        Assert.DoesNotContain(snap.Todos, t => t.Id == created.Id);
    }

    [Fact]
    public async Task IfMatch_stale_rename_returns_409_with_problem_body()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/board/Daily");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(new ItemTitleRequest("Morning"), options: s_json);
        var createRes = await client.SendAsync(createReq);
        createRes.EnsureSuccessStatusCode();
        var created = (await createRes.Content.ReadFromJsonAsync<BoardItem>(s_json))!;

        var stale = created.ServerUpdatedAtUtc!.Value.AddMinutes(-5);

        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/board/Daily/{created.Id}");
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        put.Headers.TryAddWithoutValidation("If-Match", $"\"{stale:o}\"");
        put.Content = JsonContent.Create(new ItemTitleRequest("Evening"), options: s_json);
        var putRes = await client.SendAsync(put);
        Assert.Equal(HttpStatusCode.Conflict, putRes.StatusCode);
        var body = await putRes.Content.ReadAsStringAsync();
        Assert.Contains("version_conflict", body, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequestMessage IncrementRequest(string token, Guid itemId, string idempotencyKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/board/habits/{itemId}/increment");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return req;
    }

    private static async Task<(string Token, string Email)> RegisterAndLoginAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"sync-{suffix}@integration.test";
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
