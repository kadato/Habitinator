using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

/// <summary>
///     Reusable base class for local-first settings stores shared by WASM and MAUI: reads return
///     instantly from the local platform store, writes persist locally and best-effort to the server,
///     and background refreshes keep local copies in sync when authenticated.
/// </summary>
public sealed record LocalFirstSettingsOptions<TSettings>(
    string StorageKey,
    string ApiEndpoint,
    Func<string?, TSettings> Deserialize,
    Func<TSettings, string> Serialize,
    JsonSerializerOptions? SerializerOptions = null);

public abstract class LocalFirstSettingsServiceBase<TSettings> : IDisposable
    where TSettings : class
{
    private readonly IHttpClientFactory _http;
    private readonly IClientSessionProvider _sessionProvider;
    private readonly ILogger _logger;
    private readonly LocalFirstRemoteStore<TSettings> _store;
    private readonly string _storageKey;
    private readonly string _apiEndpoint;
    private readonly JsonSerializerOptions _serializerOptions;

    public event EventHandler? Changed;

    protected LocalFirstSettingsServiceBase(
        IHttpClientFactory http,
        IClientSessionProvider sessionProvider,
        ILocalSettingsStore localStore,
        ILogger logger,
        LocalFirstSettingsOptions<TSettings> options)
    {
        _http = http;
        _sessionProvider = sessionProvider;
        _logger = logger;
        _storageKey = options.StorageKey;
        _apiEndpoint = options.ApiEndpoint;
        _serializerOptions = options.SerializerOptions ?? JsonDefaults.Api;

        _store = new LocalFirstRemoteStore<TSettings>(
            key => options.Deserialize(localStore.Read(key)),
            (key, settings) => localStore.Write(key, options.Serialize(settings)),
            options.Serialize,
            logger);

        _sessionProvider.Changed += OnSessionChanged;
    }

    private HttpClient Client => _http.CreateClient("api");

    private string GetKey() => LocalFirstRemoteStore.KeyFor(_sessionProvider.Email, _storageKey);

    public Task<TSettings> GetAsync(CancellationToken cancellationToken = default)
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

    public async Task SaveAsync(TSettings settings, CancellationToken cancellationToken = default)
    {
        var key = GetKey();
        await _store.WriteLocalAsync(key, settings, cancellationToken).ConfigureAwait(false);

        if (_sessionProvider.IsLoggedIn)
        {
            await LocalFirstSaves.PutBestEffortAsync(
                Client,
                _apiEndpoint,
                settings,
                _serializerOptions,
                _logger,
                cancellationToken).ConfigureAwait(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<TSettings?> FetchRemoteAsync(CancellationToken cancellationToken)
    {
        if (!_sessionProvider.IsLoggedIn)
        {
            return null;
        }

        using var res = await Client.GetAsync(_apiEndpoint, cancellationToken).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            return null;
        }

        return await res.Content.ReadFromJsonAsync<TSettings>(_serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sessionProvider.Changed -= OnSessionChanged;
            _store.Dispose();
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
