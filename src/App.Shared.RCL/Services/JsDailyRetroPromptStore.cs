using System.Globalization;

using App.Shared.RCL.Models;

using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

public sealed class JsDailyRetroPromptStore : IDailyRetroPromptStore
{
    private readonly IJSRuntime _js;

    public JsDailyRetroPromptStore(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<DateOnly?> GetLastPromptResolvedUtcDateAsync(CancellationToken cancellationToken = default)
    {
        string? s;
        try
        {
            s = await _js.InvokeAsync<string?>("habitinatorGetDailyRetroResolved").ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
        catch (JSException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(s) || !DateOnly.TryParse(s, out var d)) return null;

        return d;
    }

    public async Task SetPromptResolvedForTodayAsync(CancellationToken cancellationToken = default)
    {
        var ymd = DailySchedule.UtcToday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        try
        {
            await _js.InvokeVoidAsync("habitinatorSetDailyRetroResolved", ymd).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
    }
}
