using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

namespace App.Shared.RCL.Services;

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
        _sessionProvider.Changed += () => Changed?.Invoke();
    }

    private HttpClient Client => _http.CreateClient("api");

    public event Action? Changed;

    public async Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var localSettings = ReadLocal();

        if (!_sessionProvider.IsLoggedIn) return localSettings;

        // Fetch remote settings in the background
        _ = Task.Run(async () =>
        {
            try
            {
                using var res = await Client.GetAsync("api/settings/notifications", cancellationToken).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    var remote = await res.Content.ReadFromJsonAsync<NotificationSettings>(Serializer, cancellationToken).ConfigureAwait(false);
                    if (remote is not null)
                    {
                        var remoteJson = NotificationSettingsJson.Serialize(remote);
                        var localJson = NotificationSettingsJson.Serialize(localSettings);
                        if (remoteJson != localJson)
                        {
                            WriteLocal(remote);
                            Changed?.Invoke();
                        }
                    }
                }
            }
            catch
            {
                // Best-effort remote sync, ignore errors in background task
            }
        }, cancellationToken);

        return localSettings;
    }

    public async Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
    {
        WriteLocal(settings);

        if (!_sessionProvider.IsLoggedIn)
        {
            Changed?.Invoke();
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
        Changed?.Invoke();
    }

    private NotificationSettings ReadLocal()
    {
        var json = _localStore.Get(PreferencesKey);
        return NotificationSettingsJson.DeserializeOrDefault(json);
    }

    private void WriteLocal(NotificationSettings settings)
    {
        _localStore.Set(PreferencesKey, NotificationSettingsJson.Serialize(settings));
    }
}
