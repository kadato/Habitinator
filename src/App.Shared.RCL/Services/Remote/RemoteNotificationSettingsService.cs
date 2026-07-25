using System.Text.Json;
using System.Text.Json.Serialization;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteNotificationSettingsService : INotificationSettingsService
{
    private const string PreferencesKey = "notification_settings_v1";

    private static readonly JsonSerializerOptions Serializer = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IClientSessionProvider _sessionProvider;
    private readonly IHttpClientFactory _http;
    private readonly ILocalSettingsStore _localStore;

    public RemoteNotificationSettingsService(
        IHttpClientFactory http,
        IClientSessionProvider sessionProvider,
        ILocalSettingsStore localStore)
    {
        _http = http;
        _sessionProvider = sessionProvider;
        _localStore = localStore;
        _sessionProvider.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    private HttpClient Client => _http.CreateClient("api");

    public event EventHandler? Changed;

    private string GetKey()
    {
        var email = _sessionProvider.Email;
        return string.IsNullOrEmpty(email) ? PreferencesKey : $"{PreferencesKey}_{email}";
    }

    public async Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var key = GetKey();
        var localSettings = ReadLocal(key);

        if (!_sessionProvider.IsLoggedIn)
        {
            return localSettings;
        }

        // Fetch remote settings in the background
        _ = Task.Run(async () =>
        {
            try
            {
                using var res = await Client.GetAsync("api/settings/notifications", CancellationToken.None).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    var remote = await res.Content.ReadFromJsonAsync<NotificationSettings>(Serializer, CancellationToken.None).ConfigureAwait(false);
                    if (remote is not null)
                    {
                        var remoteJson = NotificationSettingsJson.Serialize(remote);
                        var localJson = NotificationSettingsJson.Serialize(localSettings);
                        if (remoteJson != localJson)
                        {
                            WriteLocal(key, remote);
                            Changed?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
            }
            catch
            {
                // Best-effort remote sync, ignore errors in background task
            }
        }, CancellationToken.None);

        return localSettings;
    }

    public async Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
    {
        var key = GetKey();
        WriteLocal(key, settings);

        if (!_sessionProvider.IsLoggedIn)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            using var res = await Client
                .PutAsJsonAsync("api/settings/notifications", settings, Serializer, cancellationToken)
                .ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
        }
        catch
        {
            // Best effort save
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private NotificationSettings ReadLocal(string key)
    {
        var json = _localStore.Read(key);
        return NotificationSettingsJson.DeserializeOrDefault(json);
    }

    private void WriteLocal(string key, NotificationSettings settings)
    {
        _localStore.Write(key, NotificationSettingsJson.Serialize(settings));
    }
}
