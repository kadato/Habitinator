using App.Shared.RCL.Services;

using Microsoft.Extensions.Logging;

namespace App.MAUI.Services;

public sealed class MauiLocalSettingsStore(ILogger<MauiLocalSettingsStore> logger) : ILocalSettingsStore
{
    public string? Read(string key, string? defaultValue = null)
    {
        try
        {
            return Microsoft.Maui.Storage.Preferences.Get(key, defaultValue);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read local setting {Key}. The store returns the default.", key);
            return defaultValue;
        }
    }

    public void Write(string key, string value)
    {
        try
        {
            Microsoft.Maui.Storage.Preferences.Set(key, value);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not write local setting {Key}.", key);
        }
    }
}
