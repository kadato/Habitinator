using System.Text.Json;

namespace App.Shared.RCL.Services;

/// <summary>
///     Persists per-column board filters in local settings store so the filters survive navigation
///     and reloads. Keyed per account.
/// </summary>
public sealed class JsBoardColumnStateStore : IBoardColumnStateStore
{
    private const string BaseKey = "habitinator.columnFilters.v1";
    private readonly ILocalSettingsStore _localStore;
    private readonly IClientSessionProvider _sessionProvider;

    public JsBoardColumnStateStore(ILocalSettingsStore localStore, IClientSessionProvider sessionProvider)
    {
        _localStore = localStore;
        _sessionProvider = sessionProvider;
    }

    private string GetKey() => LocalFirstRemoteStore.KeyFor(_sessionProvider.Email, BaseKey);

    public Task<BoardColumnFilterState?> GetAsync(CancellationToken cancellationToken = default)
    {
        var raw = _localStore.Read(GetKey());
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Task.FromResult<BoardColumnFilterState?>(null);
        }

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<BoardColumnFilterState>(raw, JsonDefaults.Api));
        }
        catch
        {
            return Task.FromResult<BoardColumnFilterState?>(null);
        }
    }

    public Task SetAsync(BoardColumnFilterState state, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(state, JsonDefaults.Api);
        _localStore.Write(GetKey(), json);
        return Task.CompletedTask;
    }
}
