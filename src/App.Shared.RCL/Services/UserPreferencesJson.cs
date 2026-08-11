using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public static class UserPreferencesJson
{
    public static string Serialize(UserPreferences settings)
    {
        return SettingsJsonCodec.Serialize(settings);
    }

    public static UserPreferences DeserializeOrDefault(string? json)
    {
        return SettingsJsonCodec.DeserializeOrDefault(
            json,
            UserPreferences.CreateDefault,
            static p => p.Normalize());
    }
}
