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
    private readonly ILocalSettingsStore _localStore;
    private readonly Func<string?, TSettings> _deserialize;
    private readonly Func<TSettings, string> _serialize;
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
        _localStore = localStore;
        _deserialize = options.Deserialize;
        _serialize = options.Serialize;
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

    private string GetPendingKey() => GetKey() + "_pending";

    public Task<TSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var key = GetKey();
        var localSettings = _store.GetLocal(key);

        if (_sessionProvider.IsLoggedIn)
        {
            _ = TryFlushPendingAsync(cancellationToken);
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
            var ok = await LocalFirstSaves.TryPutAsync(
                Client,
                _apiEndpoint,
                settings,
                _serializerOptions,
                _logger,
                cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                PersistPending(settings);
            }
            else
            {
                ClearPending();
                _ = TryFlushPendingAsync(cancellationToken);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void PersistPending(TSettings settings)
    {
        try
        {
            _localStore.Write(GetPendingKey(), _serialize(settings));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not persist pending settings.");
        }
    }

    private void ClearPending()
    {
        try
        {
            _localStore.Write(GetPendingKey(), string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not clear pending settings.");
        }
    }

    private async Task TryFlushPendingAsync(CancellationToken cancellationToken)
    {
        string? raw;
        try
        {
            raw = _localStore.Read(GetPendingKey());
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        TSettings pending;
        try
        {
            pending = _deserialize(raw);
        }
        catch
        {
            ClearPending();
            return;
        }

        var ok = await LocalFirstSaves.TryPutAsync(
            Client,
            _apiEndpoint,
            pending,
            _serializerOptions,
            _logger,
            cancellationToken).ConfigureAwait(false);
        if (ok)
        {
            ClearPending();
        }
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
