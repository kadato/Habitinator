using System.Text.Json;
using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public static class NotificationSettingsJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Serialize(NotificationSettings settings) =>
        JsonSerializer.Serialize(settings, Options);

    public static NotificationSettings DeserializeOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return NotificationSettings.CreateDefault();
        }

        try
        {
            return JsonSerializer.Deserialize<NotificationSettings>(json, Options)
                   ?? NotificationSettings.CreateDefault();
        }
        catch (JsonException)
        {
            return NotificationSettings.CreateDefault();
        }
    }
}
