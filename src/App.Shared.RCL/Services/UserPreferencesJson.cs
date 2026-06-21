using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public static class UserPreferencesJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(UserPreferences settings)
    {
        return JsonSerializer.Serialize(settings, Options);
    }

    public static UserPreferences DeserializeOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return UserPreferences.CreateDefault();
        }

        try
        {
            return JsonSerializer.Deserialize<UserPreferences>(json, Options)
                   ?? UserPreferences.CreateDefault();
        }
        catch (JsonException)
        {
            return UserPreferences.CreateDefault();
        }
    }
}
