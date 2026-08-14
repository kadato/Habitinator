using System.Globalization;

using App.Shared.RCL.Models;

using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

public sealed class JsDailyRetroPromptStore : JsPerUserStoreBase, IDailyRetroPromptStore
{
    private readonly IJSRuntime _js;
    private readonly IUserTimeZoneService _timeZoneService;
    private readonly IClock _clock;

    public JsDailyRetroPromptStore(
        IJSRuntime js,
        IUserTimeZoneService timeZoneService,
        IClientSessionProvider sessionProvider,
        IClock clock)
        : base(js, sessionProvider)
    {
        _js = js;
        _timeZoneService = timeZoneService;
        _clock = clock;
    }

    protected override string BaseKey => "habitinator.dailyRetro.ymd";

    public async Task<DateOnly?> GetLastPromptResolvedLocalDateAsync(CancellationToken cancellationToken = default)
    {
        // Safe default: no persisted state, fail open. Never throw on JS failure.
        var s = await JsInvokeSafe.InvokeAsync<string?>(_js, "habitinatorGetDailyRetroResolved", GetKey());

        if (string.IsNullOrWhiteSpace(s) || !DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d))
        {
            return null;
        }

        return d;
    }

    public Task SetPromptResolvedForTodayAsync(CancellationToken cancellationToken = default)
    {
        var ymd = DailySchedule.LocalToday(_clock, _timeZoneService).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return JsInvokeSafe.InvokeVoidAsync(_js, "habitinatorSetDailyRetroResolved", GetKey(), ymd);
    }
}
