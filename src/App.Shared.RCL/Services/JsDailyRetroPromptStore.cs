using System.Globalization;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public sealed class JsDailyRetroPromptStore : IDailyRetroPromptStore
{
    private const string BaseKey = "habitinator.dailyRetro.ymd";
    private readonly ILocalSettingsStore _localStore;
    private readonly IClientSessionProvider _sessionProvider;
    private readonly IUserTimeZoneService _timeZoneService;
    private readonly IClock _clock;

    public JsDailyRetroPromptStore(
        ILocalSettingsStore localStore,
        IUserTimeZoneService timeZoneService,
        IClientSessionProvider sessionProvider,
        IClock clock)
    {
        _localStore = localStore;
        _timeZoneService = timeZoneService;
        _sessionProvider = sessionProvider;
        _clock = clock;
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

    public Task SetPromptResolvedForTodayAsync(CancellationToken cancellationToken = default)
    {
        var ymd = DailySchedule.LocalToday(_clock, _timeZoneService).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _localStore.Write(GetKey(), ymd);
        return Task.CompletedTask;
    }
}
