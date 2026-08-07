using System.Text.Json;

using App.Shared.RCL.Models;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteNotificationSettingsService : INotificationSettingsService, IDisposable
{
    private const string PreferencesKey = "notification_settings_v1";

    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;

    private readonly IClientSessionProvider _sessionProvider;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<RemoteNotificationSettingsService> _logger;
    private readonly LocalFirstRemoteStore<NotificationSettings> _store;

    public RemoteNotificationSettingsService(
        IHttpClientFactory http,
        IClientSessionProvider sessionProvider,
        ILocalSettingsStore localStore,
        ILogger<RemoteNotificationSettingsService> logger)
    {
        _http = http;
        _sessionProvider = sessionProvider;
        _logger = logger;
        _store = new LocalFirstRemoteStore<NotificationSettings>(
            key => NotificationSettingsJson.DeserializeOrDefault(localStore.Read(key)),
            (key, settings) => localStore.Write(key, NotificationSettingsJson.Serialize(settings)),
            NotificationSettingsJson.Serialize,
            logger);
        _sessionProvider.Changed += OnSessionChanged;
    }

    private HttpClient Client => _http.CreateClient("api");

    public event EventHandler? Changed;

    private string GetKey()
    {
        var email = _sessionProvider.Email;
        return string.IsNullOrEmpty(email) ? PreferencesKey : $"{PreferencesKey}_{email}";
    }

    public Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var key = GetKey();
        var localSettings = _store.GetLocal(key);

        if (_sessionProvider.IsLoggedIn)
        {
            _store.RefreshInBackground(
                key,
                localSettings,
                FetchRemoteAsync,
                () => Changed?.Invoke(this, EventArgs.Empty),
                cancellationToken);
        }

        return Task.FromResult(localSettings);
    }

    public async Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
    {
        var key = GetKey();
        await _store.WriteLocalAsync(key, settings, cancellationToken).ConfigureAwait(false);

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
        catch (Exception ex)
        {
            // Best-effort save; the local copy is already updated
            _logger.LogDebug(ex, "Failed to save notification settings to the server.");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<NotificationSettings?> FetchRemoteAsync(CancellationToken cancellationToken)
    {
        using var res = await Client.GetAsync("api/settings/notifications", cancellationToken).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            return null;
        }

        return await res.Content.ReadFromJsonAsync<NotificationSettings>(Serializer, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _sessionProvider.Changed -= OnSessionChanged;
        _store.Dispose();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
