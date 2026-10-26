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
        updated.Tags.Should().Be("work, urgent");
        updated.Notes.Should().Contain("Need quarterly stats attached.");
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
}
