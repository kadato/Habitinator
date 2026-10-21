using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

using App.Shared.RCL.Models;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class McpEndpointTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions TestJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly string[] MonWedFriWeekdays = ["Mon", "Wed", "Fri"];
    private static readonly string[] MonWedWeekdays = ["Mon", "Wed"];
    [Fact]
    public async Task Mcp_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
                @params = new { },
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var res = await client.SendAsync(request);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mcp_ListsToolsPromptsAndResources_AndCallsSnapshot()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"mcp-{suffix}@integration.test";
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
        await using var mcp = await McpClient.CreateAsync(transport);

        var tools = await mcp.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        names.Should().Contain("get_snapshot");
        names.Should().Contain("get_board_summary");
        names.Should().Contain("create_item");
        names.Should().Contain("batch_create_items");
        names.Should().Contain("habit_plus");
        names.Should().Contain("daily_complete_for_date");
        names.Should().Contain("log_timer_session");
        names.Should().Contain("export_data");
        names.Should().Contain("batch_toggle_items");
        names.Should().Contain("bulk_archive");

        var prompts = await mcp.ListPromptsAsync();
        var promptNames = prompts.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        promptNames.Should().Contain("plan_day");
        promptNames.Should().Contain("daily_standup");
        promptNames.Should().Contain("evening_review");
        promptNames.Should().Contain("backlog_triage");

        var resources = await mcp.ListResourcesAsync();
        var resourceUris = resources.Select(r => r.Uri).ToHashSet(StringComparer.Ordinal);
        resourceUris.Should().Contain("board://snapshot");
        resourceUris.Should().Contain("board://summary");
        resourceUris.Should().Contain("board://summary.md");
        resourceUris.Should().Contain("board://tags");

        var snapshot = await mcp.CallToolAsync("get_snapshot");
        snapshot.IsError.Should().Be(null);
    }

    [Fact]
    public async Task Mcp_SearchItems_FindsByTitleTagAndSection()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var title = $"Searchable {stamp} todo";
        var created = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = title }), json)!;
        await CallTextAsync(mcp, "update_todo", new()
        {
            ["itemId"] = created.Id,
            ["tags"] = $"searchable,{stamp}",
        });

        var byQuery = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "search_items", new Dictionary<string, object?> { ["query"] = stamp }), json)!;
        byQuery.Select(t => t.Id).Should().Contain(created.Id);

        var byTag = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "search_items", new Dictionary<string, object?> { ["tag"] = stamp }), json)!;
        byTag.Select(t => t.Id).Should().Contain(created.Id);

        var bySectionMiss = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "search_items", new Dictionary<string, object?> { ["query"] = stamp, ["section"] = "Habit" }), json)!;
        bySectionMiss.Should().BeEmpty();

        var names = (await mcp.ListToolsAsync()).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        names.Should().Contain("search_items");
    }

    [Fact]
    public async Task Mcp_CreateItem_AcceptsNotesTagsAndDate()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var daily = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?> { ["section"] = "Daily", ["title"] = $"MCP dated daily {stamp}", ["notes"] = "n", ["tags"] = "t", ["date"] = "2026-01-05" }), json)!;
        daily.DailyStartDate.Should().Be(new DateOnly(2026, 1, 5));
        daily.Notes.Should().Be("n");

        var todo = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = $"MCP dated todo {stamp}", ["date"] = "2026-02-06" }), json)!;
        todo.TodoDueDate.Should().Be(new DateOnly(2026, 2, 6));

        await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await mcp.CallToolAsync("create_item", new Dictionary<string, object?> { ["section"] = "Habit", ["title"] = $"MCP dated habit {stamp}", ["date"] = "2026-01-05" }));
    }

    [Fact]
    public async Task Mcp_ValidationErrors_ReturnToolErrors()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var item = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = "MCP validation todo" }), json)!;

        async Task ExpectError(string name, Dictionary<string, object?> args)
        {
            var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
                await mcp.CallToolAsync(name, args));
            ex.Message.Should().NotBeNullOrWhiteSpace($"tool {name} should report InvalidParams");
        }

        await ExpectError("update_daily", new() { ["itemId"] = item.Id, ["repeatInterval"] = 0 });
        await ExpectError("update_daily", new() { ["itemId"] = item.Id, ["counter"] = -1 });
        await ExpectError("update_daily", new() { ["itemId"] = item.Id, ["weekdays"] = 999 });
        await ExpectError("update_habit", new() { ["itemId"] = item.Id, ["counter"] = -5 });
        await ExpectError("get_sync_delta", new() { ["cursor"] = "   " });
        // delete_item and import_data without confirmation ask for elicitation.
        // The test client has no ElicitationHandler, so the client throws here.
        // Real callers either confirm via elicitation or retry with confirmToken.
        var deleteEx = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await mcp.CallToolAsync("delete_item", new Dictionary<string, object?> { ["section"] = "Todo", ["itemId"] = item.Id }));
        deleteEx.Message.Should().NotBeNullOrWhiteSpace("tool delete_item should ask for confirmation");
        var importEx = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await mcp.CallToolAsync("import_data", new Dictionary<string, object?> { ["json"] = "{}" }));
        importEx.Message.Should().NotBeNullOrWhiteSpace("tool import_data should ask for confirmation");
        await ExpectError("daily_complete_for_date", new() { ["itemId"] = item.Id, ["date"] = "not-a-date" });
        await ExpectError("get_day_detail", new() { ["date"] = "not-a-date" });
        await ExpectError("put_preferences", new() { ["dayStartLocalTime"] = "not-a-time" });
        await ExpectError("put_preferences", new() { ["pomodoroWorkMinutes"] = 999 });
        await ExpectError("put_notification_settings", new() { ["quietHoursStartUtc"] = "25:00" });
        await ExpectError("log_activity", new() { ["eventType"] = "TimerSession" });
        await ExpectError("log_activity", new() { ["eventType"] = "HabitPlus", ["boardItemId"] = Guid.NewGuid() });
        await ExpectError("log_timer_session", new() { ["durationSeconds"] = 60, ["boardItemId"] = Guid.NewGuid() });
        await ExpectError("log_timer_session", new() { ["durationSeconds"] = 60, ["customLabel"] = new string('x', 501) });
        await ExpectError("set_checklist", new() { ["section"] = "Todo", ["itemId"] = item.Id, ["json"] = "not json" });
        await ExpectError("add_checklist_item", new() { ["section"] = "Todo", ["itemId"] = item.Id, ["text"] = "   " });
    }

    [Fact]
    public async Task Mcp_Prompts_ListAllAndRejectBadDate()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var prompts = await mcp.ListPromptsAsync();
        var promptNames = prompts.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        promptNames.Should().Contain("plan_day");
        promptNames.Should().Contain("review_week");
        promptNames.Should().Contain("habit_coach");

        var action = () => mcp.GetPromptAsync("plan_day", new Dictionary<string, object?> { ["date"] = "not-a-date" });
        await action.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Mcp_Resources_ReadSnapshotAndRejectBadId()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var resources = await mcp.ListResourcesAsync();
        resources.Select(r => r.Uri).Should().Contain("board://snapshot");

        var snapshot = await mcp.ReadResourceAsync("board://snapshot");
        snapshot.Contents.Should().NotBeEmpty();

        var badItem = () => mcp.ReadResourceAsync("board://item/not-a-guid");
        await badItem.Should().ThrowAsync<Exception>();

        var badDay = () => mcp.ReadResourceAsync("activity://day/not-a-date");
        await badDay.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Mcp_FullCoverage_TodoNotesChecklistReorderTagsSettings()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        // New tools are advertised.
        var names = (await mcp.ListToolsAsync()).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        names.Should().Contain("reorder_item");
        names.Should().Contain("list_tags");
        names.Should().Contain("add_checklist_item");
        names.Should().Contain("set_checklist_item_done");
        names.Should().Contain("remove_checklist_item");
        names.Should().Contain("set_checklist");
        names.Should().Contain("get_checklist");
        names.Should().Contain("set_preferences_json");

        // Notes, tags, due date, then checklist add and check.
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60));
        var created = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = "MCP coverage todo" }), json)!;
        var updated = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "update_todo", new()
            {
                ["itemId"] = created.Id,
                ["notes"] = "coverage notes",
                ["tags"] = "mcp,coverage",
                ["dueDate"] = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }), json)!;
        updated.Notes.Should().Be("coverage notes");
        updated.Tags.Should().Be("mcp,coverage");
        updated.TodoDueDate.Should().Be(dueDate);

        var checklist = JsonSerializer.Deserialize<DailyChecklistItem[]>(
            await CallTextAsync(mcp, "add_checklist_item", new()
            {
                ["section"] = "Todo",
                ["itemId"] = created.Id,
                ["text"] = "subtask one",
            }), json)!;
        checklist.Should().HaveCount(1);
        var checkedList = JsonSerializer.Deserialize<DailyChecklistItem[]>(
            await CallTextAsync(mcp, "set_checklist_item_done", new()
            {
                ["section"] = "Todo",
                ["itemId"] = created.Id,
                ["checklistItemId"] = checklist[0].Id,
                ["done"] = true,
            }), json)!;
        checkedList[0].IsDone.Should().BeTrue();

        // Second item, then reorder the first one last and check order and tags.
        var second = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new() { ["section"] = "Todo", ["title"] = "MCP second todo" }), json)!;
        await CallTextAsync(mcp, "reorder_item", new()
        {
            ["section"] = "Todo",
            ["itemId"] = created.Id,
            ["position"] = 99,
        });
        var snapshot = JsonSerializer.Deserialize<BoardSnapshot>(
            await CallTextAsync(mcp, "get_snapshot", []), json)!;
        snapshot.Todos.Select(t => t.Id).Last().Should().Be(created.Id);
        (await CallTextAsync(mcp, "list_tags", [])).Should().Contain("mcp");

        // Daily recurrence fields and habit counters through MCP.
        // Use dates relative to today so the streak backfill always has room:
        // a start 10 days ago leaves 3 scheduled days before today for counter 3.
        var dailyStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var daily = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new() { ["section"] = "Daily", ["title"] = "MCP daily" }), json)!;
        var updatedDaily = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "update_daily", new()
            {
                ["itemId"] = daily.Id,
                ["repeat"] = "Daily",
                ["repeatInterval"] = 1,
                ["startDate"] = dailyStart,
                ["counter"] = 3,
            }), json)!;
        updatedDaily.DailyRepeat.Should().Be(DailyRepeatType.Daily);
        updatedDaily.DailyRepeatInterval.Should().Be(1);
        updatedDaily.Counter.Should().Be(3);

        var weeklyStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var weekly = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "update_daily", new()
            {
                ["itemId"] = daily.Id,
                ["repeat"] = "Weekly",
                ["repeatInterval"] = 2,
                ["weekdays"] = 65,
                ["startDate"] = weeklyStart,
            }), json)!;
        weekly.DailyRepeat.Should().Be(DailyRepeatType.Weekly);
        weekly.DailyRepeatInterval.Should().Be(2);
        weekly.DailyWeekdays.Should().Be(65);

        var habit = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new() { ["section"] = "Habit", ["title"] = "MCP habit" }), json)!;
        var updatedHabit = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "update_habit", new()
            {
                ["itemId"] = habit.Id,
                ["resetPeriod"] = "Weekly",
                ["counter"] = 4,
                ["negativeCounter"] = 1,
                ["notes"] = "habit notes",
            }), json)!;
        updatedHabit.ResetPeriod.Should().Be(HabitResetPeriod.Weekly);
        updatedHabit.Counter.Should().Be(4);
        updatedHabit.NegativeCounter.Should().Be(1);

        // Full settings round trip.
        var prefs = JsonSerializer.Deserialize<UserPreferences>(
            await CallTextAsync(mcp, "put_preferences", new()
            {
                ["displayName"] = "MCP User",
                ["theme"] = "Dark",
                ["pomodoroCyclesBeforeLongBreak"] = 6,
                ["enableKeyboardShortcuts"] = false,
            }), json)!;
        prefs.DisplayName.Should().Be("MCP User");
        prefs.Theme.Should().Be(AppTheme.Dark);
        prefs.PomodoroCyclesBeforeLongBreak.Should().Be(6);
        prefs.EnableKeyboardShortcuts.Should().BeFalse();

        var notif = JsonSerializer.Deserialize<NotificationSettings>(
            await CallTextAsync(mcp, "put_notification_settings", new()
            {
                ["dailyReminderEnabled"] = true,
                ["dailyReminderTime"] = "08:30",
                ["quietHoursEnabled"] = true,
                ["quietHoursStartUtc"] = "22:00",
                ["quietHoursEndUtc"] = "06:00",
                ["toastDuration"] = "Long",
                ["soundEnabled"] = false,
            }), json)!;
        notif.DailyReminderTime.Should().Be(TimeSpan.FromHours(8.5));
        notif.QuietHoursStartUtc.Should().Be(TimeSpan.FromHours(22));
        notif.ToastDuration.Should().Be(NotificationToastDuration.Long);
        notif.SoundEnabledForDeviceNotifications.Should().BeFalse();

        _ = second;
    }

    [Fact]
    public async Task Mcp_CreateItem_WithChecklist_AndBatchOperations()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var checklist = "[{\"text\":\"Step 1\",\"isDone\":false},{\"text\":\"Step 2\",\"isDone\":true}]";
        var created = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new()
            {
                ["section"] = "Todo",
                ["title"] = $"Atomic todo {stamp}",
                ["checklistJson"] = checklist
            }), json)!;

        created.ChecklistJson.Should().NotBeNullOrWhiteSpace();
        created.ChecklistJson.Should().Contain("Step 1");

        // Create 2 items for batch toggle and bulk archive.
        var todo1 = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new() { ["section"] = "Todo", ["title"] = $"Batch 1 {stamp}" }), json)!;
        var todo2 = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new() { ["section"] = "Todo", ["title"] = $"Batch 2 {stamp}" }), json)!;

        // Batch toggle items
        var toggled = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "batch_toggle_items", new()
            {
                ["section"] = "Todo",
                ["itemIds"] = new[] { todo1.Id, todo2.Id }
            }), json)!;

        toggled.Should().HaveCount(2);
        toggled.All(t => t.IsCompleted).Should().BeTrue();

        // Bulk archive items
        var archived = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "bulk_archive", new()
            {
                ["section"] = "Todo",
                ["itemIds"] = new[] { todo1.Id, todo2.Id }
            }), json)!;

        archived.Should().HaveCount(2);
    }

    [Fact]
    public async Task Mcp_SummaryResources_AndNewPrompts()
    {
        var mcp = await CreateLoggedInMcpClientAsync();

        // Resources
        var summary = await mcp.ReadResourceAsync("board://summary");
        summary.Contents.Should().NotBeEmpty();
        var summaryText = string.Concat(summary.Contents.OfType<TextResourceContents>().Select(c => c.Text));
        summaryText.Should().Contain("habitsCount");

        var summaryMd = await mcp.ReadResourceAsync("board://summary.md");
        summaryMd.Contents.Should().NotBeEmpty();
        var summaryMdText = string.Concat(summaryMd.Contents.OfType<TextResourceContents>().Select(c => c.Text));
        summaryMdText.Should().Contain("# Board Summary");

        var tagsResource = await mcp.ReadResourceAsync("board://tags");
        tagsResource.Contents.Should().NotBeEmpty();

        // Prompts
        var standup = await mcp.GetPromptAsync("daily_standup");
        standup.Messages.Should().NotBeEmpty();
        standup.Messages[0].Content.ToString().Should().Contain("Daily standup");

        var review = await mcp.GetPromptAsync("evening_review");
        review.Messages.Should().NotBeEmpty();
        review.Messages[0].Content.ToString().Should().Contain("Evening review");

        var triage = await mcp.GetPromptAsync("backlog_triage");
        triage.Messages.Should().NotBeEmpty();
        triage.Messages[0].Content.ToString().Should().Contain("Backlog triage");
    }

    [Fact]
    public async Task Mcp_PersonalTokenEndpoint_GeneratesUsableToken()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"mcp-token-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, password));
        register.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var initialToken = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", initialToken);

        // Generate long-lived MCP personal token
        var tokenRes = await client.PostAsJsonAsync("/api/account/mcp-token", new CreateMcpTokenRequest(Days: 60, Label: "Test Agent"));
        tokenRes.EnsureSuccessStatusCode();
        var tokenData = await tokenRes.Content.ReadFromJsonAsync<CreateMcpTokenResponse>();
        tokenData.Should().NotBeNull();
        tokenData.Token.Should().NotBeNullOrWhiteSpace();
        tokenData.Days.Should().Be(60);
        tokenData.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddDays(50));

        // Use the newly minted MCP token with McpClient
        var mcpClient = factory.CreateClient();
        mcpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.Token);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(mcpClient.BaseAddress!, "/mcp"), },
            mcpClient,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);
        await using var mcp = await McpClient.CreateAsync(transport);

        var snapshot = await mcp.CallToolAsync("get_snapshot");
        snapshot.IsError.Should().Be(null);
    }

    [Fact]
    public async Task Mcp_GetBoardSummary_ReturnsMarkdownAndJson()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var md = await CallTextAsync(mcp, "get_board_summary", []);
        md.Should().Contain("# Board Summary");
        md.Should().Contain("**Dailies:**");
        md.Should().Contain("**Todos:**");
        md.Should().Contain("**Habits:**");

        var jsonStr = await CallTextAsync(mcp, "get_board_summary", new() { ["format"] = "json" });
        jsonStr.Should().Contain("habitsCount");
        jsonStr.Should().Contain("dailiesCount");
        jsonStr.Should().Contain("todosActiveCount");
    }

    [Fact]
    public async Task Mcp_BatchCreateItems_CreatesMultipleItems()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var created = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "batch_create_items", new()
            {
                ["items"] = new object[]
                {
                    new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = $"Batch todo 1 {stamp}" },
                    new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = $"Batch todo 2 {stamp}" },
                    new Dictionary<string, object?> { ["section"] = "Daily", ["title"] = $"Batch daily {stamp}", ["repeat"] = "Weekly", ["weekdayNames"] = MonWedFriWeekdays },
                }
            }), json)!;

        created.Should().HaveCount(3);
        created.Select(i => i.Title).Should().Contain($"Batch todo 1 {stamp}");
        created.Select(i => i.Title).Should().Contain($"Batch todo 2 {stamp}");
        created.First(i => i.Title == $"Batch daily {stamp}").DailyRepeat.Should().Be(DailyRepeatType.Weekly);
    }

    [Fact]
    public async Task Mcp_CreateItem_AcceptsRepeatScheduleAndHabitSettings()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var daily = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new()
            {
                ["section"] = "Daily",
                ["title"] = $"Configured daily {stamp}",
                ["repeat"] = "Weekly",
                ["repeatInterval"] = 2,
                ["weekdayNames"] = MonWedWeekdays
            }), json)!;

        daily.DailyRepeat.Should().Be(DailyRepeatType.Weekly);
        daily.DailyRepeatInterval.Should().Be(2);

        var habit = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new()
            {
                ["section"] = "Habit",
                ["title"] = $"Configured habit {stamp}",
                ["trackMinus"] = false,
                ["resetPeriod"] = "Weekly",
                ["counter"] = 5
            }), json)!;

        habit.TrackMinus.Should().BeFalse();
        habit.ResetPeriod.Should().Be(HabitResetPeriod.Weekly);
        habit.Counter.Should().Be(5);
    }

    [Fact]
    public async Task Mcp_Mutations_AllowOmittedSection()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var todo = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new() { ["section"] = "Todo", ["title"] = $"Omitted section todo {stamp}" }), json)!;

        // Rename without section
        var renamed = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "rename_item", new() { ["itemId"] = todo.Id, ["title"] = $"Renamed todo {stamp}" }), json)!;
        renamed.Title.Should().Be($"Renamed todo {stamp}");

        // Toggle without section
        var toggled = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "toggle_item", new() { ["itemId"] = todo.Id }), json)!;
        toggled.IsCompleted.Should().BeTrue();

        // Archive without section
        var archived = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "archive_item", new() { ["itemId"] = todo.Id }), json)!;
        archived.IsArchived.Should().BeTrue();

        // Unarchive without section
        var unarchived = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "unarchive_item", new() { ["itemId"] = todo.Id }), json)!;
        unarchived.IsArchived.Should().BeFalse();

        // Delete with confirmToken without section
        var deleteRes = await CallTextAsync(mcp, "delete_item", new() { ["itemId"] = todo.Id, ["confirmToken"] = todo.Id.ToString("D") });
        deleteRes.Should().Contain("Deleted");
    }

    [Fact]
    public async Task Mcp_BoardItemMarkdown_ResourceReturnsMarkdown()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;
        var stamp = Guid.NewGuid().ToString("N")[..8];

        var todo = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new()
            {
                ["section"] = "Todo",
                ["title"] = $"Markdown resource todo {stamp}",
                ["notes"] = "Some detailed notes",
                ["tags"] = "work,urgent",
                ["checklistJson"] = "[{\"text\":\"Step 1\",\"isDone\":true},{\"text\":\"Step 2\",\"isDone\":false}]"
            }), json)!;

        var res = await mcp.ReadResourceAsync($"board://item/{todo.Id:D}.md");
        res.Contents.Should().NotBeEmpty();
        var text = ((TextResourceContents)res.Contents[0]).Text;
        text.Should().Contain($"# {todo.Title}");
        text.Should().Contain("Some detailed notes");
        text.Should().Contain("work,urgent");
        text.Should().Contain("[x] Step 1");
        text.Should().Contain("[ ] Step 2");
    }

    [Fact]
    public async Task Mcp_BulkUnarchive_AndTagManagement()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;

        var item1 = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new()
            {
                ["section"] = "Todo",
                ["title"] = "Bulk test 1",
                ["tags"] = "project,urgent",
            }), json)!;

        var item2 = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new()
            {
                ["section"] = "Todo",
                ["title"] = "Bulk test 2",
                ["tags"] = "project,urgent",
            }), json)!;

        // Bulk archive
        var archived = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "bulk_archive", new()
            {
                ["itemIds"] = new[] { item1.Id, item2.Id }
            }), json)!;
        archived.Should().HaveCount(2);

        // Bulk unarchive
        var unarchived = JsonSerializer.Deserialize<BoardItem[]>(
            await CallTextAsync(mcp, "bulk_unarchive", new()
            {
                ["itemIds"] = new[] { item1.Id, item2.Id }
            }), json)!;
        unarchived.Should().HaveCount(2);
        unarchived.All(i => !i.IsArchived).Should().BeTrue();

        // Rename tag
        var renameMsg = await CallTextAsync(mcp, "rename_tag", new()
        {
            ["oldTag"] = "urgent",
            ["newTag"] = "high-priority"
        });
        renameMsg.Should().Contain("Renamed tag");

        var tags = JsonSerializer.Deserialize<string[]>(
            await CallTextAsync(mcp, "list_tags", []), json)!;
        tags.Should().Contain("high-priority");
        tags.Should().NotContain("urgent");

        // Delete tag
        var deleteMsg = await CallTextAsync(mcp, "delete_tag", new()
        {
            ["tag"] = "high-priority"
        });
        deleteMsg.Should().Contain("Removed tag");
    }

    [Fact]
    public async Task Mcp_MarkdownFormats_AndSearchWithArchived()
    {
        var mcp = await CreateLoggedInMcpClientAsync();
        var json = TestJson;

        var item = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new()
            {
                ["section"] = "Todo",
                ["title"] = "Markdown search test",
                ["tags"] = "markdown-test",
            }), json)!;

        await CallTextAsync(mcp, "archive_item", new() { ["itemId"] = item.Id });

        // Search with includeArchived = true and markdown format
        var searchMd = await CallTextAsync(mcp, "search_items", new()
        {
            ["query"] = "Markdown search",
            ["includeArchived"] = true,
            ["format"] = "markdown"
        });
        searchMd.Should().Contain("# Search Results");
        searchMd.Should().Contain("Markdown search test");
        searchMd.Should().Contain("[Archived]");

        // Snapshot in markdown format
        var snapshotMd = await CallTextAsync(mcp, "get_snapshot", new()
        {
            ["format"] = "markdown"
        });
        snapshotMd.Should().Contain("# Board Snapshot");
    }

    [Fact]
    public async Task Mcp_TimerSummary_AndActivityEvents()
    {
        var mcp = await CreateLoggedInMcpClientAsync();

        await CallTextAsync(mcp, "log_timer_session", new()
        {
            ["durationSeconds"] = 1500,
            ["customLabel"] = "Deep work integration test"
        });

        var summary = await CallTextAsync(mcp, "get_timer_summary", []);
        summary.Should().Contain("totalMinutes");
        summary.Should().Contain("Deep work integration test");

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var eventsJson = await CallTextAsync(mcp, "list_activity_events", new()
        {
            ["startDate"] = today,
            ["endDate"] = today
        });
        eventsJson.Should().Contain("Deep work integration test");
    }

    [Fact]
    public async Task Mcp_NewResources_AndPrompts()
    {
        var mcp = await CreateLoggedInMcpClientAsync();

        // New resources
        var streaks = await mcp.ReadResourceAsync("board://streaks");
        streaks.Contents.Should().NotBeEmpty();

        var archivedMd = await mcp.ReadResourceAsync("board://archived.md");
        archivedMd.Contents.Should().NotBeEmpty();
        string.Concat(archivedMd.Contents.OfType<TextResourceContents>().Select(c => c.Text)).Should().Contain("# Archived Items");

        var dashboardMd = await mcp.ReadResourceAsync("activity://dashboard.md");
        dashboardMd.Contents.Should().NotBeEmpty();
        string.Concat(dashboardMd.Contents.OfType<TextResourceContents>().Select(c => c.Text)).Should().Contain("# Activity Dashboard");

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dayMd = await mcp.ReadResourceAsync($"activity://day/{today}.md");
        dayMd.Contents.Should().NotBeEmpty();
        string.Concat(dayMd.Contents.OfType<TextResourceContents>().Select(c => c.Text)).Should().Contain("# Activity Detail");

        // New prompts
        var created = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new() { ["section"] = "Todo", ["title"] = "Prompt test todo" }), TestJson)!;

        var breakdown = await mcp.GetPromptAsync("break_down_todo", new Dictionary<string, object?> { ["todoId"] = created.Id.ToString() });
        breakdown.Messages.Should().NotBeEmpty();
        breakdown.Messages[0].Content.ToString().Should().Contain("Break down my todo");

        var daily = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new() { ["section"] = "Daily", ["title"] = "Prompt test daily" }), TestJson)!;

        var recovery = await mcp.GetPromptAsync("streak_recovery", new Dictionary<string, object?> { ["dailyId"] = daily.Id.ToString() });
        recovery.Messages.Should().NotBeEmpty();
        recovery.Messages[0].Content.ToString().Should().Contain("Help me recover my streak");

        var retro = await mcp.GetPromptAsync("weekly_retro");
        retro.Messages.Should().NotBeEmpty();
        retro.Messages[0].Content.ToString().Should().Contain("Weekly retrospective");
    }

    private async Task<McpClient> CreateLoggedInMcpClientAsync()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"mcp-{suffix}@integration.test";
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

    private static async Task<string> CallTextAsync(
        McpClient mcp, string name, Dictionary<string, object?> args)
    {
        var result = await mcp.CallToolAsync(name, args);
        result.IsError.Should().Be(null);
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
    }
}
