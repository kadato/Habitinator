#pragma warning disable S4487, S2325, S3459, S8949, S6966 // Razor markup reads and assigns the flagged fields through @if bindings and @ref. The flagged methods call injected services and ComponentBase members. Sonar analyzes this code-behind file in isolation.
using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class MainBoard
{
    private bool _dailyRetroSessionOffered;
    private bool _dailyRetroClientReady;
    private DateTimeOffset _lastLocalMutationTime = DateTimeOffset.MinValue;
    private bool _dayChangeRefreshInFlight;
    private (bool IsOffline, bool IsSyncing, int PendingCount, string? ProblemMessage)? _lastSyncStatusKey;
    private bool _remoteRefreshInFlight;
    private DateTimeOffset _lastRemoteRefreshUtc = DateTimeOffset.MinValue;

    private void OnBoardSyncStatusChanged(object? sender, EventArgs e)
    {
        var key = (BoardSync.IsOffline, BoardSync.IsSyncing, BoardSync.PendingCount, BoardSync.SyncProblemMessage);
        if (key == _lastSyncStatusKey)
        {
            return;
        }
        _lastSyncStatusKey = key;
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnUserDayChanged(object? sender, EventArgs e)
    {
        _ = InvokeAsync(RefreshForLocalDayAsync);
    }

    /// <summary>
    ///     A board left open across the day boundary must roll over even when no row changed.
    ///     The shared day service raises this once per effective local day.
    /// </summary>
    private async Task RefreshForLocalDayAsync()
    {
        if (!_initialLoadComplete || _loadError is not null || _dayChangeRefreshInFlight)
        {
            return;
        }

        _dailyRetroSessionOffered = false;
        _dayChangeRefreshInFlight = true;
        try
        {
            // Re-normalize the rows already on screen. This resets yesterday's
            // daily checks and habit counters without waiting for the next sync.
            ApplyLocalDayToBoardLists();
            await RefreshStreaksAsync();
            if (_dailyRetroClientReady)
            {
                await TryOpenDailyYesterdayRetroIfNeededAsync();
            }
            StateHasChanged();
        }
        catch
        {
            // The next day-change notification retries the rollover.
            _dailyRetroSessionOffered = false;
        }
        finally
        {
            _dayChangeRefreshInFlight = false;
        }
    }

    /// <summary>
    ///     Moves server-pushed refreshes onto the Blazor dispatcher. SignalR and JS callbacks
    ///     do not run on the renderer sync context.
    /// </summary>
    private Task HandleRemoteBoardRefreshedAsync()
    {
        return InvokeAsync(OnRemoteBoardRefreshedAsync);
    }

    private async Task OnRemoteBoardRefreshedAsync()
    {
        // Skip a reload from a SignalR notification when a local mutation just ran.
        // That mutation already fetched the latest snapshot.
        if (DateTimeOffset.UtcNow - _lastLocalMutationTime < TimeSpan.FromSeconds(2.0))
        {
            return;
        }
        if (_remoteRefreshInFlight || DateTimeOffset.UtcNow - _lastRemoteRefreshUtc < TimeSpan.FromMilliseconds(500))
        {
            return;
        }
        _remoteRefreshInFlight = true;
        try
        {
            await LoadBoardAsync();
            // The pulled mirror already has server counters. A streak read
            // corrects streaks that decayed. Run it outside the callback so
            // other refresh subscribers are not queued behind it.
            _ = RefreshStreaksAsync();
        }
        catch
        {
            // Keep the current view. The user can use Retry.
        }
        finally
        {
            _remoteRefreshInFlight = false;
            _lastRemoteRefreshUtc = DateTimeOffset.UtcNow;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task HandleTimerLogSavedAsync(TimeSpan duration)
    {
        _lastLocalMutationTime = DateTimeOffset.UtcNow;
        var result = await TimerSessionLog.LogStoppedSessionAsync(duration);
        // When board progress was made, the undo toast for that action is the notification.
        if (result.BoardUpdateFailed || !result.BoardProgressed)
        {
            await Notifier.NotifyAsync(result.UserMessage, result.BoardUpdateFailed ? Severity.Error : Severity.Success);
        }

        await LoadBoardAsync();
    }

    /// <summary>
    ///     Waits for the first board pull so the retro prompt reads a mirror that
    ///     has already dropped the rows deleted on the server. A slow or offline
    ///     sync gives up after a few seconds and the prompt uses the local mirror.
    /// </summary>
    private async Task WaitForInitialSyncAsync()
    {
        if (BoardSync.IsPaused || BoardSync.IsOffline || BoardSync.LastSyncedUtc is not null)
        {
            return;
        }

        var synced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, EventArgs e)
        {
            if (BoardSync.LastSyncedUtc is not null || BoardSync.IsOffline)
            {
                synced.TrySetResult();
            }
        }

        BoardSync.Changed += OnChanged;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await synced.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Slow or offline. The local mirror is the best data available.
        }
        finally
        {
            BoardSync.Changed -= OnChanged;
        }
    }

    private async Task TryOpenDailyYesterdayRetroIfNeededAsync()
    {
        if (_dailyRetroSessionOffered)
        {
            return;
        }

        _dailyRetroSessionOffered = true;
        try
        {
            // The prompt can start before the board's background init because the
            // prerendered snapshot marks the load complete. Initialize the day
            // service so "yesterday" is the user's local day, not UTC.
            await UserDay.InitializeAsync();

            var today = UserDay.Today;
            var lastResolved = await DailyRetroStore.GetLastPromptResolvedLocalDateAsync();

            if (lastResolved == today)
            {
                return;
            }

            // The coordinator pulls once right after the first board load, and that
            // pull drops rows the server deleted. Wait for it, then reload the local
            // mirror so a demo reseed or a delete from another device cannot offer a
            // daily the server no longer has. The wait is capped so a slow network
            // never holds the prompt for long.
            await WaitForInitialSyncAsync();
            await LoadBoardAsync(usePrerenderedState: false);

            var missed = DailySchedule.GetYesterdayUncompletedDailies(Dailies, today, TimeZoneService);
            if (missed.Count == 0)
            {
                return;
            }

            var yesterdayDate = today.AddDays(-1);
            var parameters = new DialogParameters<DailyYesterdayRetroDialog>
            {
                { x => x.DueOn, yesterdayDate },
                { x => x.Items, missed }
            };

            var dialog = await DialogService.ShowAsync<DailyYesterdayRetroDialog>(
                string.Empty, parameters, DialogDefaults.SmallEditorNoEscape);
            var result = await dialog.Result;

            try
            {
                await DailyRetroStore.SetPromptResolvedForTodayAsync();
            }
            catch (Exception)
            {
                // Ignore save errors for the daily retro status. It is local UI state only.
            }

            if (result is { Canceled: false, Data: List<Guid> committed } && committed.Count > 0)
            {
                _lastLocalMutationTime = DateTimeOffset.UtcNow;

                // The dialog already wrote the completions to the local mirror
                // and moved the local Counter with them. Paint that at once.
                // The server-corrected streak arrives when the outbox sync
                // returns, so the visible result never waits on the sync.
                await LoadBoardAsync();
                _ = SyncAfterRetroAsync();
                await InvokeAsync(StateHasChanged);

                var label = committed.Count == 1 ? "1 daily" : $"{committed.Count} dailies";
                await Notifier.NotifyAsync($"You logged {label} for yesterday.", Severity.Success);
            }
            else
            {
                await LoadBoardAsync();
                await RefreshStreaksAsync();
            }
        }
        catch (Exception)
        {
            _dailyRetroSessionOffered = false;
        }
    }

    /// <summary>
    ///     Drains the retro completions and repaints the counters the server
    ///     calculated. Runs after the dialog closes so the Save button never
    ///     waits for the network. Each drained operation patches its local row
    ///     with the server streak, so one local read is enough and no extra
    ///     streak call is needed.
    /// </summary>
    private async Task SyncAfterRetroAsync()
    {
        if (BoardSync.IsPaused)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await BoardSyncRequestor.SyncNowAsync(cts.Token);
        }
        catch
        {
            // Offline or slow. The periodic sync retries. A partial drain
            // patched the rows it delivered, so the repaint runs in both cases.
        }

        try
        {
            await InvokeAsync(async () =>
            {
                await LoadBoardAsync();
                StateHasChanged();
            });
        }
        catch
        {
            // The board was disposed while the sync ran.
        }
    }

    private async Task RefreshStreaksAsync()
    {
        try
        {
            var streaks = await BoardDataService.GetStreakMapAsync();
            var anyChanged = false;
            for (var i = 0; i < Dailies.Count; i++)
            {
                var daily = Dailies[i];
                if (streaks.TryGetValue(daily.Id, out var s) && s != daily.Counter)
                {
                    Dailies[i] = daily with { Counter = s };
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                RecomputeFilters();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception)
        {
            // Ignore the error. The next full board load refreshes the streaks.
        }
    }
}
