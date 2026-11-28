using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Web.Data;
using App.Web.Services;

using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class AiAssistantIntegrationTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task AiTest_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/ai/test", new AiTestConnectionRequest
        {
            Endpoint = "https://opencode.ai/inference/go/openai/v1",
            Model = "mimo-v2.5"
        });

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AiChat_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/ai/chat", new AiChatRequest
        {
            Messages = [AiChatMessage.User("Hello")]
        });

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AiTestConnection_WithRegisteredUser_ExecutesConnectionTest()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"ai-test-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, password));
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();

        var loginData = await login.Content.ReadFromJsonAsync<LoginResponse>(s_json);
        loginData.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(loginData);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginData.AccessToken);

        // Test with live OpenCode Go endpoint and Muse Spark model
        var testRes = await client.PostAsJsonAsync("/api/ai/test", new AiTestConnectionRequest
        {
            Endpoint = "https://opencode.ai/inference/go/openai/v1",
            Model = "muse-spark-1.3-contributor",
            ApiKey = "test-mock-key"
        });

        testRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await testRes.Content.ReadFromJsonAsync<AiTestConnectionResponse>(s_json);
        result.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(result);
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("muse-spark-1.3-contributor");
        result.LatencyMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AiAssistantService_DirectTestConnection_SucceedsWithMuseSpark()
    {
        using var scope = factory.Services.CreateScope();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();

        var res = await aiService.TestConnectionAsync(new AiTestConnectionRequest
        {
            Endpoint = "https://opencode.ai/inference/go/openai/v1",
            Model = "muse-spark-1.3-contributor",
            ApiKey = "test-mock-key"
        });

        res.Success.Should().BeTrue();
        res.Message.Should().Contain("muse-spark-1.3-contributor");
        res.LatencyMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AiAssistantService_DirectTestConnection_SucceedsWithMimo()
    {
        using var scope = factory.Services.CreateScope();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();

        var res = await aiService.TestConnectionAsync(new AiTestConnectionRequest
        {
            Endpoint = "https://opencode.ai/inference/go/openai/v1",
            Model = "mimo-v2.5",
            ApiKey = "test-mock-key"
        });

        res.Success.Should().BeTrue();
        res.Message.Should().Contain("mimo-v2.5");
        res.LatencyMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AiChat_ExecutesBoardSummaryTool_EndToEnd_MuseSpark()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"ai-chat-muse-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, password));
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();

        var loginData = await login.Content.ReadFromJsonAsync<LoginResponse>(s_json);
        loginData.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(loginData);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginData.AccessToken);

        // Create a test habit on the board first
        var createHabitRes = await client.PostAsJsonAsync("/api/board/Habit", new ItemTitleRequest("Daily Morning Meditation"));
        createHabitRes.EnsureSuccessStatusCode();

        // Ask the AI assistant about our board using muse-spark-1.3-contributor
        var chatRes = await client.PostAsJsonAsync("/api/ai/chat", new AiChatRequest
        {
            EndpointOverride = "https://opencode.ai/inference/go/openai/v1",
            ModelOverride = "muse-spark-1.3-contributor",
            ApiKeyOverride = "test-mock-key",
            Messages =
            [
                AiChatMessage.User("What habits do I have on my board?")
            ]
        });

        chatRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var chatResult = await chatRes.Content.ReadFromJsonAsync<AiChatResponse>(s_json);
        chatResult.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(chatResult);
        chatResult.Success.Should().BeTrue();
        chatResult.ToolExecutions.Should().NotBeEmpty();
        chatResult.ToolExecutions.Any(t => t.ToolName is "get_board_summary" or "get_snapshot").Should().BeTrue();
        chatResult.Message.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(chatResult.Message);
        chatResult.Message.Content.Should().Contain("Meditation");
    }

    [Fact]
    public async Task AiChat_ExecutesBoardSummaryTool_EndToEnd_Mimo()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"ai-chat-mimo-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, password));
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();

        var loginData = await login.Content.ReadFromJsonAsync<LoginResponse>(s_json);
        loginData.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(loginData);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginData.AccessToken);

        // Create a test habit on the board first
        var createHabitRes = await client.PostAsJsonAsync("/api/board/Habit", new ItemTitleRequest("Evening Reading"));
        createHabitRes.EnsureSuccessStatusCode();

        // Ask the AI assistant about our board using mimo-v2.5
        var chatRes = await client.PostAsJsonAsync("/api/ai/chat", new AiChatRequest
        {
            EndpointOverride = "https://opencode.ai/inference/go/openai/v1",
            ModelOverride = "mimo-v2.5",
            ApiKeyOverride = "test-mock-key",
            Messages =
            [
                AiChatMessage.User("What habits do I have on my board?")
            ]
        });

        chatRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var chatResult = await chatRes.Content.ReadFromJsonAsync<AiChatResponse>(s_json);
        chatResult.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(chatResult);
        chatResult.Success.Should().BeTrue();
        chatResult.ToolExecutions.Should().NotBeEmpty();
        chatResult.ToolExecutions.Any(t => t.ToolName is "get_board_summary" or "get_snapshot").Should().BeTrue();
        chatResult.Message.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(chatResult.Message);
        chatResult.Message.Content.Should().Contain("Reading");
    }

    [Fact]
    public async Task AiChatStream_Returns_SseEvents()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"ai-chat-stream-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, password));
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();

        var loginData = await login.Content.ReadFromJsonAsync<LoginResponse>(s_json);
        loginData.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(loginData);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginData.AccessToken);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/ai/chat/stream")
        {
            Content = JsonContent.Create(new AiChatRequest
            {
                EndpointOverride = "https://opencode.ai/inference/go/openai/v1",
                ModelOverride = "mimo-v2.5",
                ApiKeyOverride = "test-mock-key",
                Messages = [AiChatMessage.User("Hello! Please reply with 'OK'")]
            })
        };

        var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new System.IO.StreamReader(stream);

        var eventsReceived = new List<AiChatStreamEvent>();
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var json = line[6..].Trim();
                if (string.IsNullOrWhiteSpace(json) || json == "[DONE]")
                {
                    continue;
                }

                var evt = JsonSerializer.Deserialize<AiChatStreamEvent>(json, s_json);
                if (evt is not null)
                {
                    eventsReceived.Add(evt);
                }
            }
        }

        eventsReceived.Should().NotBeEmpty();
        eventsReceived.Should().Contain(e => e.Type == "done");
    }

    [Fact]
    public async Task AiAssistantService_ExecuteToolCall_create_items()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"tool-create-{suffix}@test.local", Email = $"tool-create-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        const string argsJson = """
            {
                "items": [
                    { "title": "Hydrate 2L", "section": "Habit", "notes": "Drink water", "tags": "health" },
                    { "title": "Buy milk", "section": "Todo", "tags": "errands" }
                ]
            }
            """;

        var result = await aiService.ExecuteToolAsync(user.Id, "create_items", argsJson, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Result.Should().Contain("Successfully created 2 items");

        var snapshot = await boards.GetSnapshotAsync(user.Id, CancellationToken.None);
        snapshot.Habits.Should().Contain(h => h.Title == "Hydrate 2L" && h.Tags == "health");
        snapshot.Todos.Should().Contain(t => t.Title == "Buy milk" && t.Tags == "errands");
    }

    [Fact]
    public async Task AiAssistantService_ExecuteToolCall_update_tags_and_append_notes()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"tool-tags-{suffix}@test.local", Email = $"tool-tags-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var created = await boards.CreateItemAsync(user.Id, BoardSection.Todo, "Finish report", itemId: null, cancellationToken: CancellationToken.None);

        var updateTagsArgs = $$"""{ "itemId": "{{created.Id}}", "tags": "work, urgent" }""";
        var tagResult = await aiService.ExecuteToolAsync(user.Id, "update_tags", updateTagsArgs, CancellationToken.None);
        tagResult.IsSuccess.Should().BeTrue();

        var appendNotesArgs = $$"""{ "itemId": "{{created.Id}}", "notes": "Need quarterly stats attached." }""";
        var notesResult = await aiService.ExecuteToolAsync(user.Id, "append_notes", appendNotesArgs, CancellationToken.None);
        notesResult.IsSuccess.Should().BeTrue();

        var snapshot = await boards.GetSnapshotAsync(user.Id, CancellationToken.None);
        var updated = snapshot.Todos.First(t => t.Id == created.Id);
        updated.Tags.Should().Be("work,urgent");
        updated.Notes.Should().Contain("Need quarterly stats attached.");
    }

    [Fact]
    public async Task AiAssistantService_CreateItem_RejectsUnknownSection()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"tool-bad-section-{suffix}@test.local", Email = $"tool-bad-section-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var result = await aiService.ExecuteToolAsync(
            user.Id,
            "create_item",
            """{"section":"Banana","title":"Wrong section"}""",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Result.Should().Contain("unknown section");
    }

    [Fact]
    public async Task AiAssistantService_CreateItems_RejectsOversizedBatch()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"tool-batch-{suffix}@test.local", Email = $"tool-batch-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var items = string.Join(",", Enumerable.Range(0, 51).Select(i => $$"""{"section":"Todo","title":"Item {{i}}"}"""));
        var result = await aiService.ExecuteToolAsync(
            user.Id,
            "create_items",
            $$"""{"items":[{{items}}]}""",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Result.Should().Contain("at most 50");
    }

    [Fact]
    public async Task AiAssistantService_RejectsPrivateEndpoint()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"ai-ssrf-{suffix}@test.local", Email = $"ai-ssrf-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var result = await aiService.ChatAsync(
            user.Id,
            new AiPreferences(),
            new AiChatRequest
            {
                EndpointOverride = "http://127.0.0.1:11434/v1",
                ApiKeyOverride = "test-key",
                Messages = [AiChatMessage.User("Hello")]
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("HTTPS");
    }

    [Fact]
    public async Task AiAssistantService_RejectsSharedKeyForCustomHost()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"ai-shared-{suffix}@test.local", Email = $"ai-shared-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var result = await aiService.ChatAsync(
            user.Id,
            new AiPreferences(),
            new AiChatRequest
            {
                EndpointOverride = "https://example.com/v1",
                Messages = [AiChatMessage.User("Hello")]
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("shared AI key");
    }

    [Fact]
    public async Task AiAssistantService_ExecuteToolCall_set_daily_weekdays()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"tool-weekdays-{suffix}@test.local", Email = $"tool-weekdays-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var created = await boards.CreateItemAsync(user.Id, BoardSection.Daily, "Gym Workout", itemId: null, cancellationToken: CancellationToken.None);

        var weekdaysArgs = $$"""{ "itemId": "{{created.Id}}", "days": ["Monday", "Wednesday", "Friday"] }""";
        var result = await aiService.ExecuteToolAsync(user.Id, "set_daily_weekdays", weekdaysArgs, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var snapshot = await boards.GetSnapshotAsync(user.Id, CancellationToken.None);
        var updated = snapshot.Dailies.First(d => d.Id == created.Id);
        updated.DailyRepeat.Should().Be(DailyRepeatType.Weekly);
        updated.DailyWeekdays.Should().Be(DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday));
    }

    [Fact]
    public async Task AiAssistantService_ExecuteToolCall_log_past_activity_backfills_the_local_day()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();
        var stats = scope.ServiceProvider.GetRequiredService<ActivityStatisticsService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"tool-backfill-{suffix}@test.local", Email = $"tool-backfill-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var daily = await boards.CreateItemAsync(user.Id, BoardSection.Daily, "Morning walk", itemId: null, cancellationToken: CancellationToken.None);
        var today = await boards.TodayAsync(user.Id, CancellationToken.None);
        var yesterday = today.AddDays(-1);

        var result = await aiService.ExecuteToolAsync(
            user.Id,
            "log_past_activity",
            $$"""{"eventType":"DailyComplete","itemId":"{{daily.Id}}","date":"{{yesterday:yyyy-MM-dd}}","label":"Walked 30 minutes"}""",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Result.Should().Contain("DailyComplete").And.Contain(yesterday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var detail = await stats.GetActivityDayDetailAsync(user.Id, yesterday, null, CancellationToken.None);
        detail.Events.Should().ContainSingle(e =>
            e.EventType == ActivityEventType.DailyComplete &&
            e.BoardItemTitle == "Morning walk" &&
            e.CustomLabel == "Walked 30 minutes");

        var todayDetail = await stats.GetActivityDayDetailAsync(user.Id, today, null, CancellationToken.None);
        todayDetail.Events.Should().NotContain(e => e.EventType == ActivityEventType.DailyComplete);
    }

    [Fact]
    public async Task AiAssistantService_ExecuteToolCall_list_activity_events_reads_the_requested_range()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"tool-events-{suffix}@test.local", Email = $"tool-events-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var habit = await boards.CreateItemAsync(user.Id, BoardSection.Habit, "Read", itemId: null, cancellationToken: CancellationToken.None);
        var today = await boards.TodayAsync(user.Id, CancellationToken.None);
        var yesterday = today.AddDays(-1);
        await boards.LogActivityOnDateAsync(
            user.Id,
            ActivityEventType.HabitPlus,
            habit.Id,
            yesterday,
            "Finished a chapter",
            eventId: null,
            CancellationToken.None);

        var result = await aiService.ExecuteToolAsync(
            user.Id,
            "list_activity_events",
            $$"""{"startDate":"{{yesterday:yyyy-MM-dd}}","endDate":"{{yesterday:yyyy-MM-dd}}"}""",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Result.Should().Contain("HabitPlus").And.Contain("Read").And.Contain("Finished a chapter");

        var badRange = await aiService.ExecuteToolAsync(
            user.Id,
            "list_activity_events",
            $$"""{"startDate":"{{today:yyyy-MM-dd}}","endDate":"{{yesterday:yyyy-MM-dd}}"}""",
            CancellationToken.None);
        badRange.IsSuccess.Should().BeFalse();
        badRange.Result.Should().Contain("on or before");
    }

    [Fact]
    public async Task ActivityEvents_PagingReturnsEachEventOnce()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();
        var stats = scope.ServiceProvider.GetRequiredService<ActivityStatisticsService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"events-page-{suffix}@test.local", Email = $"events-page-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var habit = await boards.CreateItemAsync(user.Id, BoardSection.Habit, "Read", itemId: null, cancellationToken: CancellationToken.None);
        var today = await boards.TodayAsync(user.Id, CancellationToken.None);
        var yesterday = today.AddDays(-1);
        await boards.LogActivityOnDateAsync(
            user.Id, ActivityEventType.HabitPlus, habit.Id, yesterday, "First", eventId: null, CancellationToken.None);
        await boards.LogActivityOnDateAsync(
            user.Id, ActivityEventType.HabitPlus, habit.Id, yesterday, "Second", eventId: null, CancellationToken.None);

        var first = await stats.GetActivityEventsAsync(
            user.Id, yesterday, yesterday, limit: 1, offset: 0, cancellationToken: CancellationToken.None);
        var second = await stats.GetActivityEventsAsync(
            user.Id, yesterday, yesterday, limit: 1, offset: 1, cancellationToken: CancellationToken.None);
        var beyond = await stats.GetActivityEventsAsync(
            user.Id, yesterday, yesterday, limit: 1, offset: 2, cancellationToken: CancellationToken.None);

        first.Should().ContainSingle();
        second.Should().ContainSingle();
        first[0].CustomLabel.Should().NotBe(second[0].CustomLabel);
        first[0].CustomLabel.Should().BeOneOf("First", "Second");
        second[0].CustomLabel.Should().BeOneOf("First", "Second");
        beyond.Should().BeEmpty();
    }

    [Fact]
    public async Task AiAssistantService_ExecuteToolCall_get_activity_overview_reports_past_work()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();
        var stats = scope.ServiceProvider.GetRequiredService<ActivityStatisticsService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"tool-overview-{suffix}@test.local", Email = $"tool-overview-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var habit = await boards.CreateItemAsync(user.Id, BoardSection.Habit, "Meditate", itemId: null, cancellationToken: CancellationToken.None);
        var today = await boards.TodayAsync(user.Id, CancellationToken.None);

        // Prime the statistics cache before the backfill. Logging must invalidate it.
        var before = await stats.GetOverviewAsync(user.Id, "r370", null, CancellationToken.None);
        before.Dashboard.TotalEvents.Should().Be(0);

        await boards.LogActivityOnDateAsync(
            user.Id,
            ActivityEventType.HabitPlus,
            habit.Id,
            today.AddDays(-1),
            customLabel: null,
            eventId: null,
            CancellationToken.None);

        var result = await aiService.ExecuteToolAsync(
            user.Id,
            "get_activity_overview",
            """{"period":"r370"}""",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Result.Should().Contain("Total events: 1");
        result.Result.Should().Contain("Meditate");
        result.Result.Should().Contain(habit.Id.ToString());

        var afterFocus = await stats.GetOverviewAsync(user.Id, "r370", null, CancellationToken.None);
        afterFocus.Dashboard.TotalEvents.Should().Be(1);
    }

    [Fact]
    public async Task AiAssistantService_ExecuteToolCall_log_past_activity_rejects_today_and_wrong_sections()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"tool-backfill-guard-{suffix}@test.local", Email = $"tool-backfill-guard-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var habit = await boards.CreateItemAsync(user.Id, BoardSection.Habit, "Stretch", itemId: null, cancellationToken: CancellationToken.None);
        var today = await boards.TodayAsync(user.Id, CancellationToken.None);

        var todayResult = await aiService.ExecuteToolAsync(
            user.Id,
            "log_past_activity",
            $$"""{"eventType":"HabitPlus","itemId":"{{habit.Id}}","date":"{{today:yyyy-MM-dd}}"}""",
            CancellationToken.None);
        todayResult.IsSuccess.Should().BeFalse();
        todayResult.Result.Should().Contain("past date");

        var futureResult = await aiService.ExecuteToolAsync(
            user.Id,
            "log_past_activity",
            $$"""{"eventType":"HabitPlus","itemId":"{{habit.Id}}","date":"{{today.AddDays(1):yyyy-MM-dd}}"}""",
            CancellationToken.None);
        futureResult.IsSuccess.Should().BeFalse();

        var wrongSection = await aiService.ExecuteToolAsync(
            user.Id,
            "log_past_activity",
            $$"""{"eventType":"DailyComplete","itemId":"{{habit.Id}}","date":"{{today.AddDays(-1):yyyy-MM-dd}}"}""",
            CancellationToken.None);
        wrongSection.IsSuccess.Should().BeFalse();
        wrongSection.Result.Should().Contain("does not apply");

        var missingItem = await aiService.ExecuteToolAsync(
            user.Id,
            "log_past_activity",
            $$"""{"eventType":"HabitPlus","itemId":"{{Guid.NewGuid()}}","date":"{{today.AddDays(-1):yyyy-MM-dd}}"}""",
            CancellationToken.None);
        missingItem.IsSuccess.Should().BeFalse();
        missingItem.Result.Should().Contain("not found");
    }

    [Fact]
    public async Task Logging_a_timer_session_invalidates_the_statistics_cache()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardPersistenceService>();
        var stats = scope.ServiceProvider.GetRequiredService<ActivityStatisticsService>();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"timer-stats-{suffix}@test.local", Email = $"timer-stats-{suffix}@test.local" };
        await userManager.CreateAsync(user, "TestUser1!Aa");

        var habit = await boards.CreateItemAsync(user.Id, BoardSection.Habit, "Deep work", itemId: null, cancellationToken: CancellationToken.None);
        var before = await stats.GetOverviewAsync(user.Id, "r370", null, CancellationToken.None);
        before.Dashboard.TotalFocusMinutes.Should().Be(0);

        await boards.LogTimerSessionAsync(user.Id, TimeSpan.FromMinutes(25), habit.Id, "Focus block", eventId: null, CancellationToken.None);

        var after = await stats.GetOverviewAsync(user.Id, "r370", null, CancellationToken.None);
        after.Dashboard.TotalFocusMinutes.Should().Be(25);
    }

    [Fact]
    public async Task AiModels_WithoutToken_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/ai/models", new AiModelsRequest());

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AiModels_Returns_the_providers_list_with_curated_names()
    {
        var client = await CreateSignedInClientAsync();

        var res = await client.PostAsJsonAsync("/api/ai/models", new AiModelsRequest
        {
            Provider = "OpenCode Go",
            Endpoint = AiPreferences.DefaultOpenCodeGoEndpoint
        });

        res.EnsureSuccessStatusCode();
        var result = await res.Content.ReadFromJsonAsync<AiModelsResponse>(s_json);
        result.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(result);

        result.Success.Should().BeTrue();
        result.Source.Should().Be("provider");
        result.Models.Select(m => m.Id).Should().Equal("mimo-v2.5", "new-provider-model");
        result.Models.Single(m => m.Id == "mimo-v2.5").DisplayName.Should().Be("mimo-v2.5");
        result.Models.Single(m => m.Id == "new-provider-model").DisplayName.Should().Be("new-provider-model");
    }

    [Fact]
    public async Task AiModels_falls_back_to_the_catalog_for_a_rejected_endpoint()
    {
        var client = await CreateSignedInClientAsync();

        var res = await client.PostAsJsonAsync("/api/ai/models", new AiModelsRequest
        {
            Provider = "Ollama",
            Endpoint = "http://127.0.0.1:11434/v1"
        });

        res.EnsureSuccessStatusCode();
        var result = await res.Content.ReadFromJsonAsync<AiModelsResponse>(s_json);
        result.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(result);

        result.Success.Should().BeFalse();
        result.Source.Should().Be("catalog");
        result.Error.Should().Contain("HTTPS");
        result.Models.Should().NotBeEmpty();
        result.Models.Select(m => m.Id).Should().Contain("llama3.2");
    }

    private async Task<HttpClient> CreateSignedInClientAsync()
    {
        var client = factory.CreateClient();
        var email = $"ai-models-{Guid.NewGuid():N}@integration.test";
        const string password = "TestUser1!Aa";

        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();

        var loginData = await login.Content.ReadFromJsonAsync<LoginResponse>(s_json);
        loginData.Should().NotBeNull();
        ArgumentNullException.ThrowIfNull(loginData);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginData.AccessToken);
        return client;
    }
}
