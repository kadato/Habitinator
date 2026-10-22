using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

namespace App.Shared.Tests;

public sealed class AiPreferencesTests
{
    [Fact]
    public void AiPreferences_defaults_to_OpenCodeGo_and_muse_model()
    {
        var prefs = new AiPreferences();
        Assert.True(prefs.Enabled);
        Assert.Equal("OpenCode Go", prefs.Provider);
        Assert.Equal("https://opencode.ai/inference/go/openai/v1", prefs.Endpoint);
        Assert.Equal("muse-spark-1.3-contributor", prefs.Model);
        Assert.Empty(prefs.ApiKey);
    }

    [Fact]
    public void Normalize_trims_and_falls_back_to_defaults()
    {
        var prefs = new AiPreferences
        {
            Provider = "   ",
            Endpoint = "   ",
            Model = "   ",
            ApiKey = "  sk-test-key  ",
            CustomInstructions = new string('x', 3000)
        };

        var normalized = prefs.Normalize();

        Assert.Equal(AiPreferences.DefaultProvider, normalized.Provider);
        Assert.Equal(AiPreferences.DefaultOpenCodeEndpoint, normalized.Endpoint);
        Assert.Equal(AiPreferences.DefaultModel, normalized.Model);
        Assert.Equal("sk-test-key", normalized.ApiKey);
        Assert.Equal(2000, normalized.CustomInstructions?.Length);
    }

    [Fact]
    public void UserPreferences_roundtrip_preserves_ai_settings()
    {
        var original = new UserPreferences
        {
            DisplayName = "Test User",
            Ai = new AiPreferences
            {
                Enabled = true,
                Provider = "OpenRouter",
                Endpoint = "https://openrouter.ai/api/v1",
                ApiKey = "sk-or-v1-abc",
                Model = "anthropic/claude-3.5-haiku",
                CustomInstructions = "Be brief."
            }
        };

        var json = UserPreferencesJson.Serialize(original);
        var back = UserPreferencesJson.DeserializeOrDefault(json);

        Assert.NotNull(back.Ai);
        Assert.True(back.Ai.Enabled);
        Assert.Equal("OpenRouter", back.Ai.Provider);
        Assert.Equal("https://openrouter.ai/api/v1", back.Ai.Endpoint);
        Assert.Equal("sk-or-v1-abc", back.Ai.ApiKey);
        Assert.Equal("anthropic/claude-3.5-haiku", back.Ai.Model);
        Assert.Equal("Be brief.", back.Ai.CustomInstructions);
    }

    [Fact]
    public void AiChatMessage_factories_create_expected_roles()
    {
        var user = AiChatMessage.User("Hello");
        var assistant = AiChatMessage.Assistant("World");
        var system = AiChatMessage.System("Instructions");
        var tool = AiChatMessage.Tool("call_123", "get_board_summary", "Summary output");

        Assert.Equal("user", user.Role);
        Assert.Equal("Hello", user.Content);

        Assert.Equal("assistant", assistant.Role);
        Assert.Equal("World", assistant.Content);

        Assert.Equal("system", system.Role);
        Assert.Equal("Instructions", system.Content);

        Assert.Equal("tool", tool.Role);
        Assert.Equal("call_123", tool.ToolCallId);
        Assert.Equal("get_board_summary", tool.Name);
        Assert.Equal("Summary output", tool.Content);

        var assistantWithModel = AiChatMessage.Assistant("Response text", "muse-spark-1.3-contributor");
        Assert.Equal("assistant", assistantWithModel.Role);
        Assert.Equal("Response text", assistantWithModel.Content);
        Assert.Equal("muse-spark-1.3-contributor", assistantWithModel.Model);
    }

    [Fact]
    public void AiModelCatalog_helpers_resolve_names_and_short_names()
    {
        Assert.Equal("Muse Spark 1.3 Contributor", AiModelCatalog.GetModelDisplayName("muse-spark-1.3-contributor"));
        Assert.Equal("Muse Spark 1.3 Contributor (muse-spark-1.3-contributor)", AiModelCatalog.GetModelFullName("muse-spark-1.3-contributor"));
        Assert.Equal("Muse Spark 1.3..", AiModelCatalog.GetModelShortName("muse-spark-1.3-contributor"));

        Assert.Equal("mimo-v2.5", AiModelCatalog.GetModelDisplayName("mimo-v2.5"));
        Assert.Equal("mimo-v2.5", AiModelCatalog.GetModelFullName("mimo-v2.5"));
        Assert.Equal("mimo-v2.5", AiModelCatalog.GetModelShortName("mimo-v2.5"));

        Assert.Equal("Qwen 3.8 Flash", AiModelCatalog.GetModelDisplayName("qwen3.8-flash"));
        Assert.Equal("Qwen 3.8 Flash (qwen3.8-flash)", AiModelCatalog.GetModelFullName("qwen3.8-flash"));
        Assert.Equal("Qwen 3.8 Flash", AiModelCatalog.GetModelShortName("qwen3.8-flash"));

        // Fallbacks for empty / null
        Assert.Equal("Habitinator AI", AiModelCatalog.GetModelDisplayName(null));
        Assert.Equal("Habitinator AI", AiModelCatalog.GetModelFullName(null));
        Assert.Equal("AI", AiModelCatalog.GetModelShortName(null));
    }
}
