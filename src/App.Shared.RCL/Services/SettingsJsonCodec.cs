using System.Text.Json;

namespace App.Shared.RCL.Services;

/// <summary>Generic JSON codec for settings models with a default fallback.</summary>
public static class SettingsJsonCodec
{
    public static string Serialize<T>(T settings)
    {
        return JsonSerializer.Serialize(settings, JsonDefaults.Storage);
    }

    public static T DeserializeOrDefault<T>(string? json, Func<T> createDefault, Func<T, T>? normalize = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return createDefault();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(json, JsonDefaults.Storage);
            if (parsed is null)
            {
                return createDefault();
            }

            return normalize is null ? parsed : normalize(parsed);
        }
        catch (JsonException)
        {
            return createDefault();
        }
    }
}
