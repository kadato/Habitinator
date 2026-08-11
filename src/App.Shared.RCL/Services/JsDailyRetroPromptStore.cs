using System.Globalization;

using App.Shared.RCL.Models;

using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

public sealed class JsDailyRetroPromptStore : IDailyRetroPromptStore
{
    private readonly IJSRuntime _js;
    private readonly IUserTimeZoneService _timeZoneService;
    private readonly IClientSessionProvider _sessionProvider;
    private readonly IClock _clock;

    public JsDailyRetroPromptStore(
        IJSRuntime js,
        IUserTimeZoneService timeZoneService,
        IClientSessionProvider sessionProvider,
        IClock clock)
    {
        _js = js;
        _timeZoneService = timeZoneService;
        _sessionProvider = sessionProvider;
        _clock = clock;
    }

    private string GetKey()
    {
        var email = _sessionProvider.Email;
        return string.IsNullOrEmpty(email) ? "habitinator.dailyRetro.ymd" : $"habitinator.dailyRetro.ymd_{email}";
    }

    public async Task<DateOnly?> GetLastPromptResolvedLocalDateAsync(CancellationToken cancellationToken = default)
    {
        var s = await JsInvokeSafe.InvokeAsync<string?>(_js, "habitinatorGetDailyRetroResolved", GetKey());

        if (string.IsNullOrWhiteSpace(s) || !DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d))
        {
            return null;
        }

        return d;
    }

    public async Task SetPromptResolvedForTodayAsync(CancellationToken cancellationToken = default)
    {
        var ymd = DailySchedule.LocalToday(_clock, _timeZoneService).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await JsInvokeSafe.InvokeVoidAsync(_js, "habitinatorSetDailyRetroResolved", GetKey(), ymd);
    }
}
