using App.Shared.RCL.Services;

namespace App.MAUI.Services;

public sealed class MauiLocalSettingsStore : ILocalSettingsStore
{
    public string? Read(string key, string? defaultValue = null)
    {
        return Microsoft.Maui.Storage.Preferences.Get(key, defaultValue);
    }

    public void Write(string key, string value)
    {
        Microsoft.Maui.Storage.Preferences.Set(key, value);
    }
}
