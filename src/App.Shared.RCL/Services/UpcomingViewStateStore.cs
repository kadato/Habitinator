using System.Text.Json;

namespace App.Shared.RCL.Services;

/// <summary>
///     Persists the upcoming calendar view (Day/Week/7 days/Month) and item filter
///     (All/Dailies/To-dos) in the local settings store so the tabs survive
///     navigation and reloads. Keyed per account.
/// </summary>
public sealed class UpcomingViewStateStore : IUpcomingViewStateStore
{
    private const string BaseKey = "habitinator.upcomingView.v1";
    private readonly ILocalSettingsStore _localStore;
    private readonly IClientSessionProvider _sessionProvider;

    public UpcomingViewStateStore(ILocalSettingsStore localStore, IClientSessionProvider sessionProvider)
    {
        _localStore = localStore;
        _sessionProvider = sessionProvider;
    }

    private string GetKey() => LocalFirstRemoteStore.KeyFor(_sessionProvider.Email, BaseKey);

    public Task<UpcomingViewState?> GetAsync(CancellationToken cancellationToken = default)
    {
        var raw = _localStore.Read(GetKey());
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Task.FromResult<UpcomingViewState?>(null);
        }

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<UpcomingViewState>(raw, JsonDefaults.Api));
        }
        catch
        {
            return Task.FromResult<UpcomingViewState?>(null);
        }
    }

    public Task SetAsync(UpcomingViewState state, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(state, JsonDefaults.Api);
        _localStore.Write(GetKey(), json);
        return Task.CompletedTask;
    }
}
