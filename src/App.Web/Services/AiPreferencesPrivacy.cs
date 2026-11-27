using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

namespace App.Web.Services;

/// <summary>
///     Single place where stored AI preferences are masked for clients and incoming
///     writes are merged with the key that is already stored.
/// </summary>
public static class AiPreferencesPrivacy
{
    /// <summary>Clears the key before preferences leave the server. The configured flag remains.</summary>
    public static UserPreferences MaskForClient(UserPreferences? preferences)
    {
        var clone = Clone(preferences ?? UserPreferences.CreateDefault());
        clone.Ai ??= new AiPreferences();
        clone.Ai.ApiKeyConfigured = !string.IsNullOrEmpty(clone.Ai.ApiKey);
        clone.Ai.ApiKey = string.Empty;
        clone.Ai.ClearApiKey = false;
        return clone;
    }

    /// <summary>
    ///     Prepares preferences for storage. A blank incoming key keeps the stored key,
    ///     <see cref="AiPreferences.ClearApiKey" /> removes it, and a typed key replaces it.
    /// </summary>
    public static UserPreferences ApplyWrite(
        UserPreferences incoming,
        UserPreferences? existing,
        AiApiKeyProtector protector)
    {
        var result = Clone(incoming);
        result.Ai ??= new AiPreferences();
        result.Ai.Normalize();

        var existingKey = existing?.Ai?.ApiKey ?? string.Empty;
        if (result.Ai.ClearApiKey)
        {
            result.Ai.ApiKey = string.Empty;
        }
        else if (string.IsNullOrWhiteSpace(result.Ai.ApiKey))
        {
            result.Ai.ApiKey = protector.Protect(existingKey);
        }
        else
        {
            result.Ai.ApiKey = protector.Protect(result.Ai.ApiKey);
        }

        result.Ai.ApiKeyConfigured = !string.IsNullOrEmpty(result.Ai.ApiKey);
        result.Ai.ClearApiKey = false;
        return result;
    }

    /// <summary>Returns a provider-ready clone with the decrypted key, or an empty key.</summary>
    public static AiPreferences UnprotectForProvider(AiPreferences? preferences, AiApiKeyProtector protector)
    {
        var clone = CloneAi(preferences ?? new AiPreferences());
        clone.ApiKey = protector.Unprotect(clone.ApiKey);
        clone.ApiKeyConfigured = !string.IsNullOrEmpty(clone.ApiKey);
        clone.ClearApiKey = false;
        return clone;
    }

    private static UserPreferences Clone(UserPreferences preferences) =>
        UserPreferencesJson.DeserializeOrDefault(UserPreferencesJson.Serialize(preferences));

    private static AiPreferences CloneAi(AiPreferences preferences) =>
        UserPreferencesJson.DeserializeOrDefault(UserPreferencesJson.Serialize(new UserPreferences { Ai = preferences })).Ai
        ?? new AiPreferences();
}
