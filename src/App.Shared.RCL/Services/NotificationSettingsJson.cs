using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public static class NotificationSettingsJson
{
    public static string Serialize(NotificationSettings settings)
    {
        return JsonSerializer.Serialize(settings, JsonDefaults.Storage);
    }

    public static NotificationSettings DeserializeOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return NotificationSettings.CreateDefault();
        }

        try
        {
            return JsonSerializer.Deserialize<NotificationSettings>(json, JsonDefaults.Storage)
                   ?? NotificationSettings.CreateDefault();
        }
        catch (JsonException)
        {
            return NotificationSettings.CreateDefault();
        }
    }
}
