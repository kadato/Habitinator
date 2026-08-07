using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public static class UserPreferencesJson
{
    public static string Serialize(UserPreferences settings)
    {
        return JsonSerializer.Serialize(settings, JsonDefaults.Storage);
    }

    public static UserPreferences DeserializeOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return UserPreferences.CreateDefault();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<UserPreferences>(json, JsonDefaults.Storage);
            return parsed?.Normalize() ?? UserPreferences.CreateDefault();
        }
        catch (JsonException)
        {
            return UserPreferences.CreateDefault();
        }
    }
}
