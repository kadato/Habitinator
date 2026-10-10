using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

using App.Shared.RCL.Models;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class McpEndpointTests(PostgresWebAppFactory factory)
{
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
        names.Should().Contain("create_item");
        names.Should().Contain("habit_plus");
        names.Should().Contain("daily_complete_for_date");
        names.Should().Contain("log_timer_session");
        names.Should().Contain("export_data");

        var prompts = await mcp.ListPromptsAsync();
        prompts.Select(p => p.Name).Should().Contain("plan_day");

        var resources = await mcp.ListResourcesAsync();
        resources.Select(r => r.Uri).Should().Contain("board://snapshot");

        var snapshot = await mcp.CallToolAsync("get_snapshot");
        snapshot.IsError.Should().Be(null);
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
        var created = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new Dictionary<string, object?> { ["section"] = "Todo", ["title"] = "MCP coverage todo" }), json)!;
        var updated = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "update_todo", new()
            {
                ["itemId"] = created.Id,
                ["notes"] = "coverage notes",
                ["tags"] = "mcp,coverage",
                ["dueDate"] = "2026-12-01",
            }), json)!;
        updated.Notes.Should().Be("coverage notes");
        updated.Tags.Should().Be("mcp,coverage");
        updated.TodoDueDate.Should().Be(new DateOnly(2026, 12, 1));

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
        var daily = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "create_item", new() { ["section"] = "Daily", ["title"] = "MCP daily" }), json)!;
        var updatedDaily = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "update_daily", new()
            {
                ["itemId"] = daily.Id,
                ["repeat"] = "Daily",
                ["repeatInterval"] = 1,
                ["startDate"] = "2026-10-10",
                ["counter"] = 3,
            }), json)!;
        updatedDaily.DailyRepeat.Should().Be(DailyRepeatType.Daily);
        updatedDaily.DailyRepeatInterval.Should().Be(1);
        updatedDaily.Counter.Should().Be(3);

        var weekly = JsonSerializer.Deserialize<BoardItem>(
            await CallTextAsync(mcp, "update_daily", new()
            {
                ["itemId"] = daily.Id,
                ["repeat"] = "Weekly",
                ["repeatInterval"] = 2,
                ["weekdays"] = 65,
                ["startDate"] = "2026-10-01",
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
