using System.Text.Json;

using App.Shared.RCL.Models;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

/// <summary>
///     Local-first user preferences shared by the WASM web client and MAUI: reads return instantly
///     from the platform store, saves persist locally and best-effort to the server, and a background
///     refresh keeps the local copy aligned with the server.
/// </summary>
public sealed class LocalFirstUserPreferencesService : IUserPreferencesService, IDisposable
{
    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;

    private readonly IHttpClientFactory _http;
    private readonly IUserPreferencesLocalStore _store;
    private readonly ILogger<LocalFirstUserPreferencesService> _logger;
    private readonly LocalFirstRemoteStore<UserPreferences> _remoteStore;

    public event EventHandler? Changed;

    public LocalFirstUserPreferencesService(
        IHttpClientFactory http,
        IUserPreferencesLocalStore store,
        ILogger<LocalFirstUserPreferencesService> logger)
    {
        _http = http;
        _store = store;
        _logger = logger;
        _remoteStore = new LocalFirstRemoteStore<UserPreferences>(
            store.ReadLocal,
            store.WriteLocal,
            UserPreferencesJson.Serialize,
            logger);
        store.SessionChanged += () => Changed?.Invoke(this, EventArgs.Empty);
    }

    private HttpClient Client => _http.CreateClient("api");

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        await _store.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var key = await _store.GetKeyAsync(cancellationToken).ConfigureAwait(false);
        var localPrefs = _store.ReadLocal(key);

        if (_store.IsLoggedIn)
        {
            _remoteStore.RefreshInBackground(
                key,
                localPrefs,
                FetchRemoteAsync,
                () => Changed?.Invoke(this, EventArgs.Empty),
                cancellationToken);
        }

        return localPrefs;
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        await _store.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        var key = await _store.GetKeyAsync(cancellationToken).ConfigureAwait(false);
        await _remoteStore.WriteLocalAsync(key, preferences, cancellationToken).ConfigureAwait(false);

        if (_store.IsLoggedIn)
        {
            await LocalFirstSaves.PutBestEffortAsync(
                Client,
                "api/settings/preferences",
                preferences,
                Serializer,
                _logger,
                cancellationToken).ConfigureAwait(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<UserPreferences?> FetchRemoteAsync(CancellationToken cancellationToken)
    {
        if (!_store.IsLoggedIn)
        {
            return null;
        }

        using var res = await Client.GetAsync("api/settings/preferences", cancellationToken).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            return null;
        }

        return await res.Content.ReadFromJsonAsync<UserPreferences>(Serializer, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _remoteStore.Dispose();
    }
}
