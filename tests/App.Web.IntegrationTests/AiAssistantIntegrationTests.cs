using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Web.Services;

using FluentAssertions;

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
            Model = "muse-spark-1.3-contributor"
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
            Model = "muse-spark-1.3-contributor"
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
            Model = "mimo-v2.5"
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
}
