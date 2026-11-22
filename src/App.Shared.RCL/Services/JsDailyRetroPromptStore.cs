using System.Globalization;

namespace App.Shared.RCL.Services;

public sealed class JsDailyRetroPromptStore : IDailyRetroPromptStore
{
    private const string BaseKey = "habitinator.dailyRetro.ymd";
    private readonly ILocalSettingsStore _localStore;
    private readonly IClientSessionProvider _sessionProvider;
    private readonly IUserDayService _userDay;

    public JsDailyRetroPromptStore(
        ILocalSettingsStore localStore,
        IClientSessionProvider sessionProvider,
        IUserDayService userDay)
    {
        _localStore = localStore;
        _sessionProvider = sessionProvider;
        _userDay = userDay;
    }

    private string GetKey() => LocalFirstRemoteStore.KeyFor(_sessionProvider.Email, BaseKey);

    public Task<DateOnly?> GetLastPromptResolvedLocalDateAsync(CancellationToken cancellationToken = default)
    {
        var s = _localStore.Read(GetKey());
        if (string.IsNullOrWhiteSpace(s) || !DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d))
        {
            return Task.FromResult<DateOnly?>(null);
        }

        return Task.FromResult<DateOnly?>(d);
    }

    public async Task SetPromptResolvedForTodayAsync(CancellationToken cancellationToken = default)
    {
        await _userDay.InitializeAsync(cancellationToken);
        var ymd = _userDay.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _localStore.Write(GetKey(), ymd);
    }
}
