using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public static class NotificationSettingsJson
{
    public static string Serialize(NotificationSettings settings)
    {
        return SettingsJsonCodec.Serialize(settings);
    }

    public static NotificationSettings DeserializeOrDefault(string? json)
    {
        return SettingsJsonCodec.DeserializeOrDefault(
            json,
            NotificationSettings.CreateDefault);
    }
}
