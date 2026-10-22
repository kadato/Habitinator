namespace App.Shared.RCL.Models;

/// <summary>Preferences for the in-app AI assistant and external provider integration.</summary>
public sealed class AiPreferences
{
    public const string DefaultProvider = "OpenCode Go";
    public const string DefaultOpenCodeGoEndpoint = "https://opencode.ai/inference/go/openai/v1";
    public const string DefaultOpenCodeEndpoint = "https://opencode.ai/inference/go/openai/v1";
    public const string DefaultOpenCodeConsoleEndpoint = "https://opencode.ai/inference/openai/v1";
    public const string DefaultOpenRouterEndpoint = "https://openrouter.ai/api/v1";
    public const string DefaultOpenAiEndpoint = "https://api.openai.com/v1";
    public const string DefaultGroqEndpoint = "https://api.groq.com/openai/v1";
    public const string DefaultOllamaEndpoint = "http://localhost:11434/v1";
    public const string DefaultModel = "muse-spark-1.3-contributor";

    public bool Enabled { get; set; } = true;

    public string Provider { get; set; } = DefaultProvider;

    public string Endpoint { get; set; } = DefaultOpenCodeGoEndpoint;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = DefaultModel;

    public string? CustomInstructions { get; set; }

    public AiPreferences Normalize()
    {
        Provider = string.IsNullOrWhiteSpace(Provider) ? DefaultProvider : Provider.Trim();
        Endpoint = string.IsNullOrWhiteSpace(Endpoint) ? DefaultOpenCodeGoEndpoint : Endpoint.Trim();
        Model = string.IsNullOrWhiteSpace(Model) ? DefaultModel : Model.Trim();
        ApiKey = ApiKey?.Trim() ?? string.Empty;
        if (CustomInstructions is not null && CustomInstructions.Length > 2000)
        {
            CustomInstructions = CustomInstructions[..2000];
        }
        return this;
    }
}
