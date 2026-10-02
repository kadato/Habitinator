using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;
using App.Shared.RCL.Services.Remote;
using App.Web.Data;
using App.Web.Services;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        after2.Counter.Should().Be(after1.Counter);
        after2.ServerUpdatedAtUtc.Should().Be(after1.ServerUpdatedAtUtc);
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
        secondRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
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
            {
                msg.EnsureSuccessStatusCode();
            }
        }
        finally
        {
            foreach (var msg in responses)
            {
                msg.Dispose();
            }
        }

        using var snapReq = new HttpRequestMessage(HttpMethod.Get, "/api/board/");
        snapReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var snapRes = await client.SendAsync(snapReq);
        snapRes.EnsureSuccessStatusCode();
        var snap = (await snapRes.Content.ReadFromJsonAsync<BoardSnapshot>(s_json))!;
        snap.Habits.Should().ContainSingle(h => h.Id == created.Id)
            .Which.Counter.Should().Be(1);
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
        delta.DeletedItemIds.Should().Contain(created.Id);

        using var snapReq = new HttpRequestMessage(HttpMethod.Get, "/api/board/");
        snapReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var snapRes = await client.SendAsync(snapReq);
        snapRes.EnsureSuccessStatusCode();
        var snap = (await snapRes.Content.ReadFromJsonAsync<BoardSnapshot>(s_json))!;
        snap.Todos.Should().NotContain(t => t.Id == created.Id);
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
        putRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await putRes.Content.ReadAsStringAsync();
        body.Should().ContainEquivalentOf("version_conflict");
    }

    [Fact]
    public async Task Reorder_proximity_triggers_rebalancing()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        // 1. Create three todos
        var t1 = await CreateTodoAsync(client, token, "Todo 1");
        var t2 = await CreateTodoAsync(client, token, "Todo 2");
        await CreateTodoAsync(client, token, "Todo 3");

        // 2. Fetch the snapshot to see initial sort orders
        var snapshot = await GetSnapshotAsync(client, token);
        var todo1 = snapshot.Todos.First(x => x.Id == t1.Id);

        // 3. Update the second item's sort order to be extremely close to the first item's, a 1e-12 difference
        var targetSortOrder = todo1.SortOrder - 1e-12;
        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/board/todos/{t2.Id}");
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        put.Content = JsonContent.Create(new TodoUpdateRequest("Todo 2", null, null, null, null, targetSortOrder), options: s_json);
        var putRes = await client.SendAsync(put);
        putRes.EnsureSuccessStatusCode();

        // 4. Fetch the snapshot again
        var updatedSnapshot = await GetSnapshotAsync(client, token);

        // 5. Verify they are rebalanced to sequential values, e.g. 1.0, 2.0, 3.0
        var sortedTodos = updatedSnapshot.Todos.OrderBy(x => x.SortOrder).ToList();
        sortedTodos.Count.Should().BeGreaterThanOrEqualTo(3);

        // Check that the gap between all consecutive elements in the list is exactly 1.0. This indicates sequential rebalancing.
        for (var i = 0; i < sortedTodos.Count - 1; i++)
        {
            (sortedTodos[i + 1].SortOrder - sortedTodos[i].SortOrder).Should().BeApproximately(1.0, 0.0001);
        }
    }

    [Fact]
    public async Task Daily_reorder_proximity_triggers_rebalancing()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        var d1 = await CreateDailyAsync(client, token, "Daily 1");
        var d2 = await CreateDailyAsync(client, token, "Daily 2");
        await CreateDailyAsync(client, token, "Daily 3");

        var snapshot = await GetSnapshotAsync(client, token);
        var daily1 = snapshot.Dailies.First(x => x.Id == d1.Id);

        var targetSortOrder = daily1.SortOrder - 1e-12;
        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/board/dailies/{d2.Id}");
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        put.Content = JsonContent.Create(
            new DailyUpdateRequest("Daily 2", null, null, null, DailyRepeatType.Daily, 1, null, 0, targetSortOrder),
            options: s_json);
        var putRes = await client.SendAsync(put);
        var body = await putRes.Content.ReadAsStringAsync();
        putRes.IsSuccessStatusCode.Should().BeTrue($"reorder returned {(int)putRes.StatusCode}: {body}");

        var updatedSnapshot = await GetSnapshotAsync(client, token);
        var sortedDailies = updatedSnapshot.Dailies.OrderBy(x => x.SortOrder).ToList();
        sortedDailies.Count.Should().BeGreaterThanOrEqualTo(3);
        for (var i = 0; i < sortedDailies.Count - 1; i++)
        {
            (sortedDailies[i + 1].SortOrder - sortedDailies[i].SortOrder).Should().BeApproximately(1.0, 0.0001);
        }
    }

    [Fact]
    public async Task Oversized_idempotency_key_returns_400()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/board/Habit");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(new ItemTitleRequest("Water"), options: s_json);
        var createRes = await client.SendAsync(createReq);
        createRes.EnsureSuccessStatusCode();
        var created = (await createRes.Content.ReadFromJsonAsync<BoardItem>(s_json))!;

        using var inc = IncrementRequest(token, created.Id, new string('k', 129));
        var res = await client.SendAsync(inc);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().ContainEquivalentOf("idempotency_key_too_long");
    }

    [Fact]
    public async Task Oversized_idempotency_key_is_rejected_at_service_level_without_touching_db()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (_, email) = await RegisterAndLoginAsync(client);

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var lookup = await dbFactory.CreateDbContextAsync();
        var userId = await lookup.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();

        using var scope = factory.Services.CreateScope();
        var idem = scope.ServiceProvider.GetRequiredService<BoardIdempotencyService>();

        var executed = false;
        var (statusCode, body, _) = await idem.RunAsync(
            userId,
            new string('k', 129),
            BoardIdempotencyService.ComputeFingerprintHex("POST", "/api/board/habits/x/increment", "{}"),
            () =>
            {
                executed = true;
                return Task.FromResult<(int statusCode, string body, string? contentType)>((200, "{}", "application/json"));
            },
            CancellationToken.None);

        statusCode.Should().Be(400);
        body.Should().ContainEquivalentOf("idempotency_key_too_long");
        executed.Should().BeFalse();

        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.BoardRequestIdempotencies.AnyAsync(x => x.UserId == userId && x.IdempotencyKey.Length == 129))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Idempotency_key_at_max_length_reaches_execute()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (_, email) = await RegisterAndLoginAsync(client);

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var lookup = await dbFactory.CreateDbContextAsync();
        var userId = await lookup.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();

        using var scope = factory.Services.CreateScope();
        var idem = scope.ServiceProvider.GetRequiredService<BoardIdempotencyService>();

        var key = new string('k', BoardIdempotencyService.MaxIdempotencyKeyLength);
        var (statusCode, _, _) = await idem.RunAsync(
            userId,
            key,
            BoardIdempotencyService.ComputeFingerprintHex("POST", "/p", "{}"),
            () => Task.FromResult<(int statusCode, string body, string? contentType)>((200, "{}", "application/json")),
            CancellationToken.None);

        statusCode.Should().Be(200);

        await using var cleanup = await dbFactory.CreateDbContextAsync();
        var row = await cleanup.BoardRequestIdempotencies.SingleOrDefaultAsync(x => x.UserId == userId && x.IdempotencyKey == key);
        if (row is not null)
        {
            cleanup.BoardRequestIdempotencies.Remove(row);
            await cleanup.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Empty_idempotency_key_bypasses_store()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (_, email) = await RegisterAndLoginAsync(client);

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var lookup = await dbFactory.CreateDbContextAsync();
        var userId = await lookup.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();

        using var scope = factory.Services.CreateScope();
        var idem = scope.ServiceProvider.GetRequiredService<BoardIdempotencyService>();

        foreach (var empty in new string?[] { null, "", "   " })
        {
            var calls = 0;
            var (statusCode, _, _) = await idem.RunAsync(
                userId,
                empty,
                "fp",
                () =>
                {
                    calls++;
                    return Task.FromResult<(int statusCode, string body, string? contentType)>((200, "ok", "application/json"));
                },
                CancellationToken.None);

            statusCode.Should().Be(200);
            calls.Should().Be(1);
        }
    }

    [Fact]
    public async Task Stale_tracked_entity_returns_conflict_instead_of_server_error()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, email) = await RegisterAndLoginAsync(client);
        var daily = await CreateDailyAsync(client, token, "Tracked");

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var otherWriter = await dbFactory.CreateDbContextAsync();
        var userId = await otherWriter.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();

        using var scope = factory.Services.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Load the row into the scoped context, then let another writer move the version on.
        _ = await scopedDb.BoardItems.SingleAsync(x => x.Id == daily.Id);
        var row = await otherWriter.BoardItems.SingleAsync(x => x.Id == daily.Id);
        row.Title = "Changed elsewhere";
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await otherWriter.SaveChangesAsync();

        // The service still sees its stale tracked instance and saves over the new version.
        var service = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();
        var result = await service.UpdateDailyAsync(
            userId,
            daily.Id,
            new UpdateDailyArgs("Tracked", null, null, null, DailyRepeatType.Daily, 1, null, 0, null),
            CancellationToken.None);

        result.Status.Should().Be(BoardMutationStatus.Conflict);
        result.Item?.Title.Should().Be("Changed elsewhere");
    }

    [Fact]
    public async Task Failed_mutation_does_not_leave_a_stuck_idempotency_claim()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, email) = await RegisterAndLoginAsync(client);
        var daily = await CreateDailyAsync(client, token, "Tracked");

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var otherWriter = await dbFactory.CreateDbContextAsync();
        var userId = await otherWriter.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();

        using var scope = factory.Services.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tracked = await scopedDb.BoardItems.SingleAsync(x => x.Id == daily.Id);

        // Move the row version so the scoped context's save conflicts.
        var row = await otherWriter.BoardItems.SingleAsync(x => x.Id == daily.Id);
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await otherWriter.SaveChangesAsync();

        var idem = scope.ServiceProvider.GetRequiredService<BoardIdempotencyService>();
        var key = Guid.NewGuid().ToString();
        var fingerprint = BoardIdempotencyService.ComputeFingerprintHex("PUT", $"/api/board/dailies/{daily.Id}", "body");

        Func<Task> act = async () =>
        {
            await idem.RunAsync(
                userId,
                key,
                fingerprint,
                async () =>
                {
                    tracked.Title = "Will fail";
                    await scopedDb.SaveChangesAsync();
                    return (200, "", "application/json");
                },
                CancellationToken.None);
        };

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        // The claim must be gone, otherwise every retry of this key waits and times out.
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.BoardRequestIdempotencies.AnyAsync(x => x.UserId == userId && x.IdempotencyKey == key))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Conflict_inside_an_idempotent_request_does_not_poison_the_claim_save()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, email) = await RegisterAndLoginAsync(client);
        var daily = await CreateDailyAsync(client, token, "Tracked");

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var otherWriter = await dbFactory.CreateDbContextAsync();
        var userId = await otherWriter.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();

        using var scope = factory.Services.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _ = await scopedDb.BoardItems.SingleAsync(x => x.Id == daily.Id);
        var row = await otherWriter.BoardItems.SingleAsync(x => x.Id == daily.Id);
        row.Title = "Changed elsewhere";
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await otherWriter.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();
        var idem = scope.ServiceProvider.GetRequiredService<BoardIdempotencyService>();

        // The mutation returns a conflict, then the idempotency service saves the claim on the same
        // context. A failed entity left tracked would make that save retry the failed update.
        var (statusCode, _, _) = await idem.RunAsync(
            userId,
            Guid.NewGuid().ToString(),
            BoardIdempotencyService.ComputeFingerprintHex("PUT", $"/api/board/dailies/{daily.Id}", "body"),
            async () =>
            {
                var result = await service.UpdateDailyAsync(
                    userId,
                    daily.Id,
                    new UpdateDailyArgs("Tracked", null, null, null, DailyRepeatType.Daily, 1, null, 0, null),
                    CancellationToken.None);
                return (409, JsonSerializer.Serialize(result.Item), "application/json");
            },
            CancellationToken.None);

        statusCode.Should().Be(409);
    }

    [Fact]
    public async Task Stale_update_from_second_context_throws_concurrency_exception()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);
        var created = await CreateTodoAsync(client, token, "Concurrent");

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db1 = await dbFactory.CreateDbContextAsync();
        await using var db2 = await dbFactory.CreateDbContextAsync();

        var row1 = await db1.BoardItems.SingleAsync(x => x.Id == created.Id);
        var row2 = await db2.BoardItems.SingleAsync(x => x.Id == created.Id);

        row1.Title = "Writer one";
        row1.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db1.SaveChangesAsync();

        row2.Title = "Writer two";
        row2.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var staleSave = async () => await db2.SaveChangesAsync();

        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Duplicate_activity_event_id_is_deduplicated()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        var eventId = Guid.NewGuid();
        var request = new ActivityLogRequest(ActivityEventType.HabitPlus, null, null, "Water", eventId);

        (await PostActivityAsync(client, token, request)).EnsureSuccessStatusCode();
        (await PostActivityAsync(client, token, request)).EnsureSuccessStatusCode();

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.UserActivityEvents.CountAsync(x => x.EventId == eventId)).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_activity_events_with_the_same_id_insert_once()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        var eventId = Guid.NewGuid();
        var request = new ActivityLogRequest(ActivityEventType.HabitPlus, null, null, "Pushups", eventId);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => PostActivityAsync(client, token, request)));
        try
        {
            foreach (var response in responses)
            {
                response.EnsureSuccessStatusCode();
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.UserActivityEvents.CountAsync(x => x.EventId == eventId)).Should().Be(1);
    }

    private static Task<HttpResponseMessage> PostActivityAsync(HttpClient client, string token, ActivityLogRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/activity/log");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = JsonContent.Create(request, options: s_json);
        return client.SendAsync(message);
    }

    [Fact]
    public async Task Daily_update_edge_case_payloads_do_not_return_server_errors()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        var cases = new (string Name, DailyUpdateRequest Request)[]
        {
            ("monthly huge streak", new DailyUpdateRequest("Edge", null, null, new DateOnly(2016, 1, 1), DailyRepeatType.Monthly, 1, null, 9999, 1.0)),
            ("daily huge streak", new DailyUpdateRequest("Edge", null, null, new DateOnly(2016, 1, 1), DailyRepeatType.Daily, 1, null, 9999, 1.0)),
            ("invalid repeat enum", new DailyUpdateRequest("Edge", null, null, new DateOnly(2026, 1, 1), (DailyRepeatType)99, 1, null, 0, 1.0)),
            ("min start date", new DailyUpdateRequest("Edge", null, null, DateOnly.MinValue, DailyRepeatType.Daily, 1, null, 0, 1.0)),
            ("max start date", new DailyUpdateRequest("Edge", null, null, DateOnly.MaxValue, DailyRepeatType.Daily, 1, null, 0, 1.0)),
            ("max counter", new DailyUpdateRequest("Edge", null, null, new DateOnly(2026, 1, 1), DailyRepeatType.Daily, 1, null, int.MaxValue, 1.0)),
            ("max interval monthly", new DailyUpdateRequest("Edge", null, null, new DateOnly(2016, 1, 1), DailyRepeatType.Monthly, 999, null, 50, 1.0)),
            ("yearly huge streak", new DailyUpdateRequest("Edge", null, null, new DateOnly(2016, 1, 1), DailyRepeatType.Yearly, 1, null, 9999, 1.0)),
        };

        var failures = new List<string>();
        foreach (var (name, request) in cases)
        {
            var daily = await CreateDailyAsync(client, token, $"Edge {name} {Guid.NewGuid():N}");
            using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/board/dailies/{daily.Id}");
            put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            put.Content = JsonContent.Create(request, options: s_json);
            using var res = await client.SendAsync(put);
            if ((int)res.StatusCode >= 500)
            {
                var body = await res.Content.ReadAsStringAsync();
                failures.Add($"{name} -> {(int)res.StatusCode}: {body[..Math.Min(body.Length, 600)]}");
            }
        }

        failures.Should().BeEmpty(string.Join("\n---\n", failures));
    }

    [Fact]
    public void Server_services_resolve_sync_requestor_for_prerender()
    {
        using var scope = factory.Services.CreateScope();
        var requestor = scope.ServiceProvider.GetService<IBoardSyncRequestor>();
        requestor.Should().NotBeNull();
        var act = () => requestor.RequestSync();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task Board_sync_payloads_carry_protocol_version()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);

        using var snapReq = new HttpRequestMessage(HttpMethod.Get, "/api/board/");
        snapReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var snapRes = await client.SendAsync(snapReq);
        snapRes.EnsureSuccessStatusCode();
        snapRes.Headers.TryGetValues("X-Board-Protocol-Version", out var snapVersions).Should().BeTrue();
        snapVersions!.Should().ContainSingle().Which.Should().Be(BoardProtocolVersion.Current.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var snapshot = await snapRes.Content.ReadFromJsonAsync<BoardSnapshot>(s_json);
        snapshot!.ProtocolVersion.Should().Be(BoardProtocolVersion.Current);

        var cursor = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        using var deltaReq = new HttpRequestMessage(HttpMethod.Get, $"/api/board/sync?cursor={Uri.EscapeDataString(cursor)}");
        deltaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var deltaRes = await client.SendAsync(deltaReq);
        deltaRes.EnsureSuccessStatusCode();
        var delta = await deltaRes.Content.ReadFromJsonAsync<BoardSyncDelta>(s_json);
        delta!.ProtocolVersion.Should().Be(BoardProtocolVersion.Current);
    }

    private static async Task<BoardItem> CreateDailyAsync(HttpClient client, string token, string title)
    {
        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/board/Daily");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(new ItemTitleRequest(title), options: s_json);
        var createRes = await client.SendAsync(createReq);
        createRes.EnsureSuccessStatusCode();
        return (await createRes.Content.ReadFromJsonAsync<BoardItem>(s_json))!;
    }

    private static async Task<BoardItem> CreateTodoAsync(HttpClient client, string token, string title)
    {
        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/board/Todo");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Content = JsonContent.Create(new ItemTitleRequest(title), options: s_json);
        var createRes = await client.SendAsync(createReq);
        createRes.EnsureSuccessStatusCode();
        return (await createRes.Content.ReadFromJsonAsync<BoardItem>(s_json))!;
    }

    private static async Task<BoardSnapshot> GetSnapshotAsync(HttpClient client, string token)
    {
        using var snapReq = new HttpRequestMessage(HttpMethod.Get, "/api/board/");
        snapReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var snapRes = await client.SendAsync(snapReq);
        snapRes.EnsureSuccessStatusCode();
        return (await snapRes.Content.ReadFromJsonAsync<BoardSnapshot>(s_json))!;
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
