using System.Globalization;

using App.Shared.RCL.Models;

using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

public sealed class JsDailyRetroPromptStore : IDailyRetroPromptStore
{
    private readonly IJSRuntime _js;
    private readonly IUserTimeZoneService _timeZoneService;
    private readonly IClientSessionProvider _sessionProvider;

    public JsDailyRetroPromptStore(IJSRuntime js, IUserTimeZoneService timeZoneService, IClientSessionProvider sessionProvider)
    {
        _js = js;
        _timeZoneService = timeZoneService;
        _sessionProvider = sessionProvider;
    }

    private string GetKey()
    {
        var email = _sessionProvider.Email;
        return string.IsNullOrEmpty(email) ? "habitinator.dailyRetro.ymd" : $"habitinator.dailyRetro.ymd_{email}";
    }

    public async Task<DateOnly?> GetLastPromptResolvedLocalDateAsync(CancellationToken cancellationToken = default)
    {
        string? s;
        try
        {
            s = await _js.InvokeAsync<string?>("habitinatorGetDailyRetroResolved", GetKey()).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
        catch (JSException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(s) || !DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d))
        {
            return null;
        }

        return d;
    }

    public async Task SetPromptResolvedForTodayAsync(CancellationToken cancellationToken = default)
    {
        var ymd = DailySchedule.LocalToday(_timeZoneService).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        try
        {
            await _js.InvokeVoidAsync("habitinatorSetDailyRetroResolved", GetKey(), ymd).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // JS Interop disconnected/failed during navigation/disposal, safe to ignore
        }
        catch (JSException)
        {
            // JS Interop disconnected/failed during navigation/disposal, safe to ignore
        }
    }
}
