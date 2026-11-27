namespace App.Shared.RCL.Services;

/// <summary>Storage keys for the in-app AI chat history. History is scoped per signed-in user.</summary>
public static class AiChatStorage
{
    /// <summary>The global key used before history was scoped. Cleared on logout.</summary>
    public const string LegacyKey = "habitinator-ai-threads";

    public static string StorageKey(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized)
            ? $"{LegacyKey}:guest"
            : $"{LegacyKey}:{normalized}";
    }
}
