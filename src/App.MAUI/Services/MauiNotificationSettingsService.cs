using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using Microsoft.Maui.Storage;

namespace App.MAUI.Services;

public sealed class MauiNotificationSettingsService : INotificationSettingsService
{
    private const string PreferencesKey = "notification_settings_v1";

    public event Action? Changed;

    public Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? json = Preferences.Get(PreferencesKey, null);
        NotificationSettings settings = NotificationSettingsJson.DeserializeOrDefault(json);
        return Task.FromResult(settings);
    }

    public Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Preferences.Set(PreferencesKey, NotificationSettingsJson.Serialize(settings));
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}
