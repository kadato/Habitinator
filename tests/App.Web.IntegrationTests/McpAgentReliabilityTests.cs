using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Mcp;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace App.Web.IntegrationTests;

/// <summary>
/// Reliability tests for the MCP board tools: actionable errors, flexible tags,
/// per-item batch results, idempotency and duplicate warnings, native checklist
/// arrays, headless confirmation guidance, device flow, dry-run validation,
/// schema-docs conformance, due digest, and tag parity.
/// </summary>
[Collection(nameof(IntegrationCollection))]
public sealed partial class McpAgentReliabilityTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions TestJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record BatchCreateItemResult(
        int Index,
        bool Ok,
        Guid? Id,
        BoardItem? Item,
        string? Field,
        string? Error,
        string? Message,
        bool DryRun);

    private sealed record DueDigestEntry(Guid Id, string Title, DateOnly? DueDate, BoardSection Section);

    private sealed record DueDigest(
        DueDigestEntry[] Overdue,
        DueDigestEntry[] DueToday,
        DueDigestEntry[] DueTomorrow,
        int Total);

    private static readonly string[] WorkUrgentTags = ["work", "urgent"];
    private static readonly string[] HomeTags = ["home"];

    [Fact]
    public async Task Mcp_Tags_AcceptArrayAndString()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var fromArray = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Grocery run {stamp}",
                ["tags"] = WorkUrgentTags,
            }), json)!;
        fromArray.Tags.Should().Be("work,urgent");

        var fromString = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Garage cleanup {stamp}",
                ["tags"] = "work, urgent",
            }), json)!;
        fromString.Tags.Should().Be("work,urgent");

        var updated = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "update_todo", new Dictionary<string, object?>
            {
                ["itemId"] = fromString.Id,
                ["tags"] = HomeTags,
            }), json)!;
        updated.Tags.Should().Be("home");

        var byTag = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "search_items", new Dictionary<string, object?> { ["tag"] = "work" }), json)!;
        byTag.Select(t => t.Id).Should().Contain(fromArray.Id);

        var tags = JsonSerializer.Deserialize<string[]>(
            await CallTextAsync(mcp, "list_tags", []), json)!;
        tags.Should().Contain("work");
        tags.Should().Contain("urgent");
    }

    [Fact]
    public async Task Mcp_Errors_NameFieldAndExpectedFormat()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var tagsEx = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await mcp.CallToolAsync("create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Invalid tags case {stamp}",
                ["tags"] = 42,
            }));
        tagsEx.Message.Should().Contain("tags");
        tagsEx.Message.Should().Contain("comma-separated");

        var dateEx = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await mcp.CallToolAsync("create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Invalid date case {stamp}",
                ["date"] = "not-a-date",
            }));
        dateEx.Message.Should().Contain("date");
        dateEx.Message.Should().Contain("yyyy-MM-dd");
    }

    [Fact]
    public async Task Mcp_BatchCreate_ReturnsPerItemResults()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var results = JsonSerializer.Deserialize<BatchCreateItemResult[]>(
            await CallTextAsync(mcp, "batch_create_items", new Dictionary<string, object?>
            {
                ["items"] = new object[]
                {
                    new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = $"Valid todo item {stamp}" },
                    new Dictionary<string, object?> { ["section"] = "Habit", ["title"] = $"Invalid habit {stamp}", ["date"] = "2026-01-05" },
                    new Dictionary<string, object?> { ["section"] = "Daily", ["title"] = $"Valid daily {stamp}" },
                }
            }), json)!;

        results.Should().HaveCount(3);
        results[0].Ok.Should().BeTrue();
        results[0].Id.Should().NotBeNull();
        results[1].Ok.Should().BeFalse();
        results[1].Field.Should().Be("date");
        results[1].Message.Should().NotBeNullOrWhiteSpace();
        results[2].Ok.Should().BeTrue();

        var found = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "search_items", new Dictionary<string, object?> { ["query"] = stamp }), json)!;
        found.Select(i => i.Title).Should().Contain($"Valid todo item {stamp}");
        found.Select(i => i.Title).Should().Contain($"Valid daily {stamp}");
        found.Select(i => i.Title).Should().NotContain($"Invalid habit {stamp}");
    }

    [Fact]
    public async Task Mcp_BatchCreate_SameKeyTwice_CreatesOnce()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var key = $"batch-{stamp}";

        Dictionary<string, object?> BatchArgs() => new()
        {
            ["items"] = new object[]
            {
                new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = $"First batch item {stamp}" },
                new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = $"Second batch item {stamp}" },
            },
            ["idempotencyKey"] = key,
        };

        var first = JsonSerializer.Deserialize<BatchCreateItemResult[]>(
            await CallTextAsync(mcp, "batch_create_items", BatchArgs()), json)!;
        first.All(r => r.Ok).Should().BeTrue();

        var second = JsonSerializer.Deserialize<BatchCreateItemResult[]>(
            await CallTextAsync(mcp, "batch_create_items", BatchArgs()), json)!;
        second.Select(r => r.Id).Should().BeEquivalentTo(first.Select(r => r.Id));

        var found = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "search_items", new Dictionary<string, object?> { ["query"] = stamp }), json)!;
        found.Should().HaveCount(2);
    }

    [Fact]
    public async Task Mcp_CreateItem_SameIdempotencyKeyTwice_ReturnsOriginal()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var key = $"item-{stamp}";

        Dictionary<string, object?> CreateArgs() => new()
        {
            ["section"] = "Todo",
            ["title"] = $"One-off task {stamp}",
            ["idempotencyKey"] = key,
        };

        var first = JsonSerializer.Deserialize<BoardItem>(await CallTextAsync(mcp, "create_item", CreateArgs()), json)!;
        var second = JsonSerializer.Deserialize<BoardItem>(await CallTextAsync(mcp, "create_item", CreateArgs()), json)!;
        second.Id.Should().Be(first.Id);

        // Same key with different fields must fail loudly instead of duplicating.
        var mismatch = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await mcp.CallToolAsync("create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Other task {stamp}",
                ["idempotencyKey"] = key,
            }));
        mismatch.Message.Should().Contain("idempotencyKey");
    }

    [Fact]
    public async Task Mcp_CreateItem_DuplicateTitle_WarnsWithExisting()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;

        var first = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = "Resume and portfolio polish",
            }), json)!;

        var dupe = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await mcp.CallToolAsync("create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = "Resume and portfolio polish",
            }));
        dupe.Message.Should().Contain("Already exists");
        dupe.Message.Should().Contain("Resume and portfolio polish");
        dupe.Message.Should().Contain(first.Id.ToString("D"));

        var forced = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = "Resume and portfolio polish",
                ["allowDuplicate"] = true,
            }), json)!;
        forced.Id.Should().NotBe(first.Id);
    }

    [Fact]
    public async Task Mcp_SetChecklist_AcceptsNativeArrayAndString()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var created = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Checklist task {stamp}",
            }), json)!;

        var fromArray = JsonSerializer.Deserialize<DailyChecklistItem[]>(
            await CallTextAsync(mcp, "set_checklist", new Dictionary<string, object?>
            {
                ["itemId"] = created.Id,
                ["json"] = new object[]
                {
                    new Dictionary<string, object?> { ["text"] = "First step", ["isDone"] = false },
                    new Dictionary<string, object?> { ["text"] = "Second step", ["isDone"] = true },
                },
            }), json)!;
        fromArray.Should().HaveCount(2);
        fromArray.Select(i => i.Text).Should().Contain(["First step", "Second step"]);
        fromArray.First(i => i.Text == "Second step").IsDone.Should().BeTrue();

        var fromString = JsonSerializer.Deserialize<DailyChecklistItem[]>(
            await CallTextAsync(mcp, "set_checklist", new Dictionary<string, object?>
            {
                ["itemId"] = created.Id,
                ["json"] = "[{\"text\":\"Third step\",\"isDone\":false}]",
            }), json)!;
        fromString.Should().HaveCount(1);
        fromString[0].Text.Should().Be("Third step");
    }

    [Fact]
    public async Task Mcp_DeleteItem_WithoutConfirm_StatesRetry()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var created = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Task to delete {stamp}",
            }), json)!;

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await mcp.CallToolAsync("delete_item", new Dictionary<string, object?> { ["itemId"] = created.Id }));
        (ex.Message.Contains("confirm", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("elicit", StringComparison.OrdinalIgnoreCase)).Should().BeTrue(
            "the error must state the confirmToken retry or ask for elicitation");
    }

    [Fact]
    public async Task Mcp_BatchCreate_DryRun_ValidatesWithoutPersisting()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var before = await SnapshotCountAsync(mcp);

        var results = JsonSerializer.Deserialize<BatchCreateItemResult[]>(
            await CallTextAsync(mcp, "batch_create_items", new Dictionary<string, object?>
            {
                ["items"] = new object[]
                {
                    new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = $"Planned todo {stamp}" },
                    new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = "   " },
                },
                ["dryRun"] = true,
            }), json)!;

        results.Should().HaveCount(2);
        results[0].Ok.Should().BeTrue();
        results[0].DryRun.Should().BeTrue();
        results[0].Id.Should().NotBeNull();
        results[1].Ok.Should().BeFalse();
        results[1].Field.Should().Be("title");

        (await SnapshotCountAsync(mcp)).Should().Be(before);

        var preview = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Planned habit {stamp}",
                ["dryRun"] = true,
            }), json)!;
        preview.Title.Should().Be($"Planned habit {stamp}");
        (await SnapshotCountAsync(mcp)).Should().Be(before);
    }

    [Fact]
    public async Task Mcp_DueDigest_ListsEachTodoOnce()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        async Task<BoardItem> CreateTodo(string title, string? date) =>
            JsonSerializer.Deserialize<BoardItem>(
                await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
                {
                    ["section"] = "Todo",
                    ["title"] = $"{title} {stamp}",
                    ["date"] = date!,
                }), json)!;

        var overdue = await CreateTodo("Overdue application", today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var dueToday = await CreateTodo("Filing due today", "today");
        var dueTomorrow = await CreateTodo("Interview tomorrow", "tomorrow");
        var later = await CreateTodo("Later plan", today.AddDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        _ = later;

        var digest = JsonSerializer.Deserialize<DueDigest>(
            await CallTextAsync(mcp, "get_due_digest", []), json)!;
        digest.Overdue.Select(e => e.Id).Should().Contain(overdue.Id);
        digest.DueToday.Select(e => e.Id).Should().Contain(dueToday.Id);
        digest.DueTomorrow.Select(e => e.Id).Should().Contain(dueTomorrow.Id);

        // New accounts also hold seeded items: scope the exactness check to this test.
        var ours = digest.Overdue.Concat(digest.DueToday)
            .Concat(digest.DueTomorrow)
            .Where(e => e.Title.Contains(stamp, StringComparison.Ordinal))
            .Select(e => e.Id)
            .ToList();
        ours.Should().HaveCount(3);
        ours.Should().OnlyHaveUniqueItems("a todo due tomorrow produces exactly one digest entry");
    }

    [Fact]
    public async Task Mcp_DueDigest_SnoozedAndCompleted_RemovedFromNextDigest()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        async Task<BoardItem> CreateTodo(string title, string? date) =>
            JsonSerializer.Deserialize<BoardItem>(
                await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
                {
                    ["section"] = "Todo",
                    ["title"] = $"{title} {stamp}",
                    ["date"] = date!,
                }), json)!;

        var dueTomorrow = await CreateTodo("Snoozable interview", "tomorrow");
        var dueToday = await CreateTodo("Completable filing", "today");

        var first = JsonSerializer.Deserialize<DueDigest>(
            await CallTextAsync(mcp, "get_due_digest", []), json)!;
        first.DueTomorrow.Select(e => e.Id).Should().Contain(dueTomorrow.Id);
        first.DueToday.Select(e => e.Id).Should().Contain(dueToday.Id);

        // Snoozing removes it from the next digest.
        var snoozed = JsonSerializer.Deserialize<DueDigest>(
            await CallTextAsync(mcp, "get_due_digest", new Dictionary<string, object?>
            {
                ["snoozedIds"] = new[] { dueTomorrow.Id },
            }), json)!;
        snoozed.Overdue.Concat(snoozed.DueToday).Concat(snoozed.DueTomorrow)
            .Select(e => e.Id).Should().NotContain(dueTomorrow.Id);

        // Completing removes it from the next digest.
        await CallTextAsync(mcp, "toggle_item", new Dictionary<string, object?> { ["itemId"] = dueToday.Id });
        var afterComplete = JsonSerializer.Deserialize<DueDigest>(
            await CallTextAsync(mcp, "get_due_digest", []), json)!;
        afterComplete.Overdue.Concat(afterComplete.DueToday).Concat(afterComplete.DueTomorrow)
            .Select(e => e.Id).Should().NotContain(dueToday.Id);
    }

    [Fact]
    public async Task Mcp_DueDigest_HonorsNotificationSettings()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        _ = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"Settings probe {stamp}",
                ["date"] = "tomorrow",
            }), json)!;

        var before = JsonSerializer.Deserialize<DueDigest>(
            await CallTextAsync(mcp, "get_due_digest", []), json)!;
        before.Total.Should().BeGreaterThan(0);

        await CallTextAsync(mcp, "put_notification_settings", new Dictionary<string, object?>
        {
            ["dailyReminderEnabled"] = false,
        });

        var muted = JsonSerializer.Deserialize<DueDigest>(
            await CallTextAsync(mcp, "get_due_digest", []), json)!;
        muted.Total.Should().Be(0);

        var reenabledJson = await CallTextAsync(mcp, "put_notification_settings", new Dictionary<string, object?>
        {
            ["dailyReminderEnabled"] = true,
        });
        var reenabled = JsonSerializer.Deserialize<NotificationSettings>(reenabledJson, json)!;
        reenabled.DailyReminderEnabled.Should().BeTrue();

        var restored = JsonSerializer.Deserialize<DueDigest>(
            await CallTextAsync(mcp, "get_due_digest", []), json)!;
        restored.Total.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DueDigest_FrozenClock_BucketsEachTodoOnce()
    {
        var frozenUtc = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);
        var clock = new FrozenClock(frozenUtc);
        var today = DailySchedule.LocalToday(clock, tz: null, dayStartLocalTime: null);
        today.Should().Be(new DateOnly(2026, 5, 15));
        var tomorrow = today.AddDays(1);

        var overdueId = Guid.NewGuid();
        var todayId = Guid.NewGuid();
        var tomorrowId = Guid.NewGuid();
        var laterId = Guid.NewGuid();
        var doneId = Guid.NewGuid();
        var stamp = "frozen";
        List<BoardItem> todos =
        [
            new(overdueId, $"Overdue {stamp}", false, 0, null, null, true, true, 0, HabitResetPeriod.Daily, null, DailyRepeatType.Daily, 1, null, null, today.AddDays(-1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, null, 0),
            new(todayId, $"Today {stamp}", false, 0, null, null, true, true, 0, HabitResetPeriod.Daily, null, DailyRepeatType.Daily, 1, null, null, today, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, null, 0),
            new(tomorrowId, $"Tomorrow {stamp}", false, 0, null, null, true, true, 0, HabitResetPeriod.Daily, null, DailyRepeatType.Daily, 1, null, null, tomorrow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, null, 0),
            new(laterId, $"Later {stamp}", false, 0, null, null, true, true, 0, HabitResetPeriod.Daily, null, DailyRepeatType.Daily, 1, null, null, today.AddDays(5), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, null, 0),
            new(doneId, $"Done {stamp}", true, 0, null, null, true, true, 0, HabitResetPeriod.Daily, null, DailyRepeatType.Daily, 1, null, null, tomorrow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, null, 0),
        ];

        var digest = ReminderTools.BuildDigest(todos, today, tomorrow, []);
        digest.Overdue.Select(e => e.Id).Should().Contain(overdueId);
        digest.DueToday.Select(e => e.Id).Should().Contain(todayId);
        digest.DueTomorrow.Select(e => e.Id).Should().Contain(tomorrowId);
        var all = digest.Overdue.Concat(digest.DueToday).Concat(digest.DueTomorrow).Select(e => e.Id).ToList();
        all.Should().HaveCount(3);
        all.Should().OnlyHaveUniqueItems("a todo due tomorrow appears exactly once in the digest");

        var snoozedDigest = ReminderTools.BuildDigest(todos, today, tomorrow, [tomorrowId]);
        snoozedDigest.Overdue.Concat(snoozedDigest.DueToday).Concat(snoozedDigest.DueTomorrow)
            .Select(e => e.Id).Should().NotContain(tomorrowId);
    }

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    [Fact]
    public async Task Mcp_CreateItem_ForceTrue_CreatesDespiteDuplicate()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        const string title = "Resume and portfolio polish";

        var first = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = title,
            }), json)!;

        var dupe = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await mcp.CallToolAsync("create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = title,
            }));
        dupe.Message.Should().Contain("Already exists");
        dupe.Message.Should().Contain(first.Id.ToString("D"));

        var forced = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = title,
                ["force"] = true,
            }), json)!;
        forced.Id.Should().NotBe(first.Id);
        forced.Title.Should().Be(title);
    }

    [Fact]
    public async Task Mcp_Tags_RoundTrip_UiAndMcp()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        // MCP -> UI: create with native array, read back canonical.
        var fromMcp = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"MCP tags {stamp}",
                ["tags"] = WorkUrgentTags,
            }), json)!;
        fromMcp.Tags.Should().Be("work,urgent");

        var tags = JsonSerializer.Deserialize<string[]>(
            await CallTextAsync(mcp, "list_tags", []), json)!;
        tags.Should().Contain("work");
        tags.Should().Contain("urgent");

        var byTag = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "search_items", new Dictionary<string, object?> { ["tag"] = "WORK" }), json)!;
        byTag.Select(t => t.Id).Should().Contain(fromMcp.Id);

        // UI -> MCP: update via REST with spaced string, MCP reads canonical unchanged.
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"tags-{suffix}@integration.test";
        const string password = "TestUser1!Aa";
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password))).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var transport2 = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(client.BaseAddress!, "/mcp") },
            client,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);
        await using var mcp2 = await McpClient.CreateAsync(transport2);

        var created = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp2, "create_item", new Dictionary<string, object?>
            {
                ["section"] = "Todo",
                ["title"] = $"REST tags {stamp}",
            }), json)!;

        // Simulate UI saving "work, urgent" (comma+space) through the REST update path.
        var updateRes = await client.PutAsJsonAsync($"/api/board/todos/{created.Id}",
            new TodoUpdateRequest(created.Title, null, "work, urgent", null, null, null),
            TestJson);
        updateRes.EnsureSuccessStatusCode();

        var viaMcp = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp2, "get_item", new Dictionary<string, object?> { ["itemId"] = created.Id }), json)!;
        viaMcp.Tags.Should().Be("work,urgent");

        var uiTags = JsonSerializer.Deserialize<string[]>(
            await CallTextAsync(mcp2, "list_tags", []), json)!;
        uiTags.Should().Contain("work");
        uiTags.Should().Contain("urgent");
    }

    [Fact]
    public async Task Board_SeedDemo_LoadsSampleDataForFreshAccount()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"fresh-{suffix}@integration.test";
        const string password = "TestUser1!Aa";
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password))).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var empty = await client.GetFromJsonAsync<BoardSnapshot>("/api/board/", TestJson);
        ArgumentNullException.ThrowIfNull(empty);
        (empty.Habits.Count + empty.Dailies.Count + empty.Todos.Count).Should().Be(0);

        var seed = await client.PostAsync("/api/board/seed-demo", null);
        seed.EnsureSuccessStatusCode();

        var seeded = await client.GetFromJsonAsync<BoardSnapshot>("/api/board/", TestJson);
        ArgumentNullException.ThrowIfNull(seeded);
        (seeded.Habits.Count + seeded.Dailies.Count + seeded.Todos.Count).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Mcp_DeviceFlow_MintsWorkingTokenWithoutPastedSecret()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"device-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password)))
            .EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var jwt = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        // 1. The headless agent starts a device authorization and shows the code.
        var start = await client.PostAsJsonAsync("/oauth/device/authorize", new { client_id = "opencode", scope = "full" });
        start.EnsureSuccessStatusCode();
        var startDoc = await start.Content.ReadFromJsonAsync<JsonElement>();
        var deviceCode = startDoc.GetProperty("device_code").GetString()!;
        var userCode = startDoc.GetProperty("user_code").GetString()!;
        startDoc.GetProperty("verification_uri").GetString().Should().EndWith("/oauth/device");
        userCode.Should().MatchRegex("^[A-Z2-9]{4}-[A-Z2-9]{4}$");
        startDoc.GetProperty("expires_in").GetInt32().Should().Be(600);
        startDoc.GetProperty("interval").GetInt32().Should().Be(5);

        // 2. Polling before approval reports pending, not a token.
        var pending = await client.PostAsJsonAsync("/oauth/token", new
        {
            grant_type = "urn:ietf:params:oauth:grant-type:device_code",
            device_code = deviceCode,
            client_id = "opencode",
        });
        pending.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pending.Content.ReadAsStringAsync()).Should().Contain("authorization_pending");

        // 3. The user approves once in the browser (authenticated, no pasted secret).
        using var approve = new HttpRequestMessage(HttpMethod.Post, "/oauth/device/approve")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["user_code"] = userCode,
                ["action"] = "approve",
            }),
        };
        approve.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var approved = await client.SendAsync(approve);
        approved.EnsureSuccessStatusCode();
        (await approved.Content.ReadAsStringAsync()).Should().Contain("Approved");

        // 4. The agent polls again and receives working tokens.
        var token = await client.PostAsJsonAsync("/oauth/token", new
        {
            grant_type = "urn:ietf:params:oauth:grant-type:device_code",
            device_code = deviceCode,
            client_id = "opencode",
        });
        token.EnsureSuccessStatusCode();
        var tokenDoc = await token.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokenDoc.GetProperty("accessToken").GetString()!;
        accessToken.Should().NotBeNullOrWhiteSpace();

        using var mcpCall = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
                @params = new { },
            }),
        };
        mcpCall.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        mcpCall.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        mcpCall.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        (await client.SendAsync(mcpCall)).IsSuccessStatusCode.Should().BeTrue();

        // 5. Refresh tokens rotate on use.
        var refreshToken = tokenDoc.TryGetProperty("refreshToken", out var rt) ? rt.GetString()! : tokenDoc.GetProperty("refresh_token").GetString()!;
        var refresh = await client.PostAsJsonAsync("/oauth/token", new
        {
            grant_type = "refresh_token",
            refresh_token = refreshToken,
            client_id = "opencode",
        });
        refresh.EnsureSuccessStatusCode();
        var refreshDoc = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        var newRefresh = refreshDoc.TryGetProperty("refreshToken", out var nrt) ? nrt.GetString()! : refreshDoc.GetProperty("refresh_token").GetString()!;
        newRefresh.Should().NotBe(refreshToken);

        var oldReuse = await client.PostAsJsonAsync("/oauth/token", new
        {
            grant_type = "refresh_token",
            refresh_token = refreshToken,
            client_id = "opencode",
        });
        oldReuse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 6. Device codes are single use and bound to the requesting client.
        var reuse = await client.PostAsJsonAsync("/oauth/token", new
        {
            grant_type = "urn:ietf:params:oauth:grant-type:device_code",
            device_code = deviceCode,
            client_id = "opencode",
        });
        reuse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reuse.Content.ReadAsStringAsync()).Should().Contain("expired_token");
    }

    [Fact]
    public async Task Mcp_Schema_MatchesDocs()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var docs = await File.ReadAllTextAsync(FindDocsMcpMd());
        var skill = await File.ReadAllTextAsync(FindSkillMd());
        var markers = McpSchemaMarkerRegex().Matches(docs);
        markers.Should().NotBeEmpty("docs/mcp.md must carry machine-readable tool schemas");
        var skillMarkers = McpSchemaMarkerRegex().Matches(skill);
        skillMarkers.Should().NotBeEmpty("agent SKILL.md must mirror the machine-readable tool schemas");

        var marked = ParseMarkers(markers);
        var skillMarked = ParseMarkers(skillMarkers);
        skillMarked.Keys.Should().BeEquivalentTo(marked.Keys, "SKILL.md markers must mirror docs/mcp.md markers");
        foreach (var name in marked.Keys)
        {
            skillMarked[name].Required.Should().BeEquivalentTo(marked[name].Required, $"SKILL.md required params of '{name}' must match docs/mcp.md");
            skillMarked[name].Optional.Should().BeEquivalentTo(marked[name].Optional, $"SKILL.md optional params of '{name}' must match docs/mcp.md");
        }

        var tools = await mcp.ListToolsAsync();
        tools.Should().NotBeEmpty();
        var liveNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            liveNames.Add(tool.Name);
            docs.Should().Contain(tool.Name, $"docs/mcp.md must mention tool '{tool.Name}'");
            skill.Should().Contain(tool.Name, $"agent SKILL.md must mention tool '{tool.Name}'");
            marked.Should().ContainKey(tool.Name, $"docs/mcp.md must carry a schema marker for tool '{tool.Name}'");

            var (properties, required) = ReadInputSchema(tool);
            var (markerRequired, markerOptional) = marked[tool.Name];
            required.Keys.Should().BeEquivalentTo(markerRequired.Keys, $"required params of '{tool.Name}' must match docs/mcp.md");
            properties.Keys.Should().BeEquivalentTo(
                markerRequired.Keys.Concat(markerOptional.Keys),
                $"all params of '{tool.Name}' must match docs/mcp.md");
            foreach (var kv in required)
            {
                markerRequired[kv.Key].Should().Be(kv.Value, $"type of required param '{kv.Key}' on '{tool.Name}' must match docs/mcp.md");
            }

            foreach (var kv in properties)
            {
                var expected = markerRequired.TryGetValue(kv.Key, out var rt) ? rt : markerOptional[kv.Key];
                expected.Should().Be(kv.Value, $"type of param '{kv.Key}' on '{tool.Name}' must match docs/mcp.md");
            }
        }

        foreach (var name in marked.Keys)
        {
            liveNames.Should().Contain(name, $"marker for removed tool '{name}' must be deleted from docs/mcp.md");
        }
    }

    private static Dictionary<string, (Dictionary<string, string> Required, Dictionary<string, string> Optional)> ParseMarkers(MatchCollection markers)
    {
        var marked = new Dictionary<string, (Dictionary<string, string> Required, Dictionary<string, string> Optional)>(StringComparer.Ordinal);
        foreach (Match marker in markers)
        {
            var name = marker.Groups["name"].Value;
            marked[name] = (
                SplitTypedParams(marker.Groups["required"].Value),
                SplitTypedParams(marker.Groups["optional"].Value));
        }

        return marked;
    }

    private static Dictionary<string, string> SplitTypedParams(string raw)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':', 2, StringSplitOptions.TrimEntries);
            if (pieces.Length == 2 && pieces[0].Length > 0 && pieces[1].Length > 0)
            {
                map[pieces[0]] = pieces[1];
            }
            else if (pieces[0].Length > 0)
            {
                map[pieces[0]] = "string";
            }
        }

        return map;
    }

    private static (Dictionary<string, string> Properties, Dictionary<string, string> Required) ReadInputSchema(McpClientTool tool)
    {
        var element = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("properties", out var props)
            && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in props.EnumerateObject())
            {
                properties[property.Name] = BaseJsonType(property.Value);
            }
        }

        var required = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("required", out var req)
            && req.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in req.EnumerateArray())
            {
                if (item.GetString() is { } name && properties.TryGetValue(name, out var t))
                {
                    required[name] = t;
                }
            }
        }

        return (properties, required);
    }

    private static string BaseJsonType(JsonElement prop)
    {
        if (prop.ValueKind == JsonValueKind.Object && prop.TryGetProperty("type", out var t))
        {
            if (t.ValueKind == JsonValueKind.String)
            {
                return t.GetString() ?? "string";
            }

            if (t.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in t.EnumerateArray())
                {
                    if (entry.GetString() is { } s && s != "null")
                    {
                        return s;
                    }
                }
            }
        }

        return "string";
    }

    private static string FindDocsMcpMd()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "mcp.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("docs/mcp.md not found above test binaries.");
    }

    private static string FindSkillMd()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "App.Web", "wwwroot", ".well-known", "agent-skills", "habitinator", "SKILL.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("SKILL.md not found above test binaries.");
    }

    [GeneratedRegex("<!-- mcp-tool-schema:\\s*(?<name>[a-z_0-9]+)\\s+required\\((?<required>[^)]*)\\)\\s+optional\\((?<optional>[^)]*)\\)\\s*-->")]
    private static partial Regex McpSchemaMarkerRegex();

    private async Task<McpClient> CreateLoggedInMcpClientAsync()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"reliable-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, password));
        register.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(client.BaseAddress!, "/mcp"), },
            client,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);
        return await McpClient.CreateAsync(transport);
    }

    private static async Task<int> SnapshotCountAsync(McpClient mcp)
    {
        var snapshot = JsonSerializer.Deserialize<BoardSnapshot>(await CallTextAsync(mcp, "get_snapshot", []), TestJson)!;
        return snapshot.Habits.Count + snapshot.Dailies.Count + snapshot.Todos.Count;
    }

    private static async Task<string> CallTextAsync(
        McpClient mcp, string name, Dictionary<string, object?> args)
    {
        var result = await mcp.CallToolAsync(name, args);
        result.IsError.Should().Be(null);
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
    }
}
