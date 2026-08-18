using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components.Web;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class GlobalTimerPanel : IDisposable
{
    private Task<IEnumerable<string>> SearchSessionTargetsAsync(string value, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Enumerable.Empty<string>());
        }

        return Task.FromResult(SearchSessionTargets(value));
    }

    protected override async Task OnInitializedAsync()
    {
        _expanded = IsSessionActive;
        TimerService.Ticked += OnTimerTicked;

        try
        {
            var prefs = await PreferencesService.GetAsync();
            TimerService.WorkDuration = TimeSpan.FromMinutes(prefs.PomodoroWorkDurationMinutes);
            TimerService.ShortBreakDuration = TimeSpan.FromMinutes(prefs.PomodoroShortBreakMinutes);
            TimerService.LongBreakDuration = TimeSpan.FromMinutes(prefs.PomodoroLongBreakMinutes);
            TimerService.IntervalsBeforeLongBreak = prefs.PomodoroCyclesBeforeLongBreak;
        }
        catch
        {
            // fallback to defaults already defined
        }
    }

    protected override void OnParametersSet()
    {
        RebuildSessionTargetOptions();
        var wasRunning = _wasRunning;
        SyncTimerFieldsFromServiceIfNeeded();

        if (TimerService.IsRunning && !wasRunning)
        {
            _expanded = true; // Auto-expand when timer starts
        }
    }

    private void SyncFocusDurationFromServiceIfChanged()
    {
        if (_focusDurationFieldInitialized && TimerService.FocusAlertAfter == _lastSyncedFocusAlertAfter)
        {
            return;
        }

        _focusDurationFieldInitialized = true;
        _lastSyncedFocusAlertAfter = TimerService.FocusAlertAfter;
        _focusDurationText = FocusDurationInput.FormatForField(TimerService.FocusAlertAfter);
        _focusDurationParseError = false;
    }

    private void SyncTimerFieldsFromServiceIfNeeded()
    {
        var key = (TimerService.TargetId, TimerService.TargetType, TimerService.BoardItemId);
        if (!_sessionTargetFieldInitialized || key != _lastSyncedSessionTarget)
        {
            _lastSyncedSessionTarget = key;
            _sessionTargetFieldInitialized = true;
            _sessionTargetText = FormatTargetForField(TimerService);
        }

        SyncFocusDurationFromServiceIfChanged();

        _wasRunning = TimerService.IsRunning;
    }

    private void OnFocusDurationBlur()
    {
        ApplyFocusDurationFromText();
    }

    private void OnFocusDurationKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or "NumpadEnter")
        {
            ApplyFocusDurationFromText();
        }
    }

    private void ApplyFocusDurationFromText()
    {
        if (string.IsNullOrWhiteSpace(_focusDurationText))
        {
            TimerService.FocusAlertAfter = null;
            _focusDurationText = string.Empty;
            _focusDurationParseError = false;
            _lastSyncedFocusAlertAfter = null;
            return;
        }

        if (!FocusDurationInput.TryParse(_focusDurationText, out var parsed)
            || parsed is null
            || parsed <= TimeSpan.Zero)
        {
            _focusDurationParseError = true;
            return;
        }

        _focusDurationParseError = false;
        TimerService.FocusAlertAfter = parsed;
        _focusDurationText = FocusDurationInput.FormatForField(parsed);
        _lastSyncedFocusAlertAfter = parsed;
    }

    private void OnTimerTicked()
    {
        _ = InvokeAsync(() =>
        {
            SyncTimerFieldsFromServiceIfNeeded();
            if (TimerService.IsRunning)
            {
                StateHasChanged();
            }
        });
    }

    private void RebuildSessionTargetOptions()
    {
        _sessionLabelToTarget.Clear();

        var all = new List<(BoardSection Section, BoardItem Item)>();
        foreach (var h in Habits)
        {
            all.Add((BoardSection.Habit, h));
        }

        foreach (var d in Dailies)
        {
            all.Add((BoardSection.Daily, d));
        }

        foreach (var t in Todos)
        {
            all.Add((BoardSection.Todo, t));
        }

        var groups = all
            .GroupBy(x => x.Item.Title, StringComparer.OrdinalIgnoreCase);

        foreach (var g in groups)
        {
            var disambiguate = g.Count() > 1;
            foreach (var (section, item) in g
                         .OrderBy(x => x.Section)
                         .ThenBy(x => x.Item.Id))
            {
                var st = section.ToString();
                var key = disambiguate
                    ? item.Title + " · " + item.Id.ToString("N")[..4]
                    : item.Title;
                if (_sessionLabelToTarget.ContainsKey(key))
                {
                    key = item.Title + " · " + item.Id.ToString("N");
                }

                _sessionLabelToTarget[key] = (st, item.Title, item.Id);
            }
        }
    }

    private string FormatTargetForField(GlobalTimerService timer)
    {
        if (string.IsNullOrEmpty(timer.TargetId))
        {
            return string.Empty;
        }

        if (timer.TargetType is "Session" or null)
        {
            return timer.TargetId;
        }

        if (timer.TargetType is "Habit" or "Daily" or "Todo")
        {
            foreach (var k in _sessionLabelToTarget.Keys)
            {
                if (_sessionLabelToTarget.TryGetValue(
                        k,
                        out var m)
                    && m.TargetType == timer.TargetType
                    && m.TargetTitle == timer.TargetId
                    && (timer.BoardItemId is null || m.ItemId == timer.BoardItemId))
                {
                    return k;
                }
            }
        }

        return timer.TargetId;
    }

    private string GetTimerTargetLeadIcon()
    {
        if (string.IsNullOrEmpty(TimerService.TargetId))
        {
            return Icons.Material.Outlined.Label;
        }

        if (string.IsNullOrEmpty(TimerService.TargetType) || TimerService.TargetType == "Session")
        {
            return BoardSectionVisuals.GetMudIconForTargetType("Session");
        }

        return BoardSectionVisuals.GetMudIconForTargetType(TimerService.TargetType);
    }

    private IEnumerable<string> SearchSessionTargets(string value)
    {
        if (_sessionLabelToTarget.Count == 0)
        {
            return Array.Empty<string>();
        }

        var v = value.Trim();
        if (v.Length == 0)
        {
            return _sessionLabelToTarget.Keys;
        }

        return _sessionLabelToTarget.Keys.Where(s =>
        {
            if (s.Contains(v, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return _sessionLabelToTarget.TryGetValue(s, out var row)
                   && row.TargetTitle.Contains(v, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void OnSessionTargetValueChanged(string? value)
    {
        var text = value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            TimerService.SetManualTarget(null);
        }
        else if (_sessionLabelToTarget.TryGetValue(text, out var mapped))
        {
            TimerService.SelectTarget(mapped.TargetType, mapped.TargetTitle, mapped.ItemId);
        }
        else
        {
            TimerService.SetManualTarget(text);
        }

        _sessionTargetText = FormatTargetForField(TimerService);
        _lastSyncedSessionTarget = (TimerService.TargetId, TimerService.TargetType, TimerService.BoardItemId);
        _sessionTargetFieldInitialized = true;
    }

    private void Start()
    {
        if (!CanStart)
        {
            return;
        }

        TimerService.Start();
        _wasRunning = true;
    }

    private void Pause()
    {
        TimerService.Pause();
        _wasRunning = false;
    }

    private void Reset()
    {
        if (TimerService.PomodoroModeEnabled)
        {
            TimerService.ResetPomodoroSession();
        }
        else
        {
            TimerService.Reset();
        }
    }

    private void SkipBreak()
    {
        TimerService.TransitionToWork();
        StateHasChanged();
    }

    private void OnPomodoroModeToggleChanged(bool enabled)
    {
        TimerService.PomodoroModeEnabled = enabled;
        if (enabled)
        {
            TimerService.ResetPomodoroSession();
        }
        else
        {
            TimerService.Reset();
        }
        _lastSyncedFocusAlertAfter = TimerService.FocusAlertAfter;
        _focusDurationText = FocusDurationInput.FormatForField(TimerService.FocusAlertAfter);
    }

    private string GetDisplayTime() => TimerService.GetDisplayTime();

    private string GetNextPomodoroLabelAndDuration()
    {
        var nextState = TimerService.CurrentPomodoroState switch
        {
            PomodoroState.Idle => "Work",
            PomodoroState.Work => (TimerService.CompletedWorkIntervalsCount + 1) % TimerService.IntervalsBeforeLongBreak == 0 ? "Long Break" : "Short Break",
            PomodoroState.ShortBreak => "Work",
            PomodoroState.LongBreak => "Work",
            _ => "Work"
        };

        var nextDuration = TimerService.CurrentPomodoroState switch
        {
            PomodoroState.Idle => TimerService.WorkDuration,
            PomodoroState.Work => (TimerService.CompletedWorkIntervalsCount + 1) % TimerService.IntervalsBeforeLongBreak == 0 ? TimerService.LongBreakDuration : TimerService.ShortBreakDuration,
            PomodoroState.ShortBreak => TimerService.WorkDuration,
            PomodoroState.LongBreak => TimerService.WorkDuration,
            _ => TimerService.WorkDuration
        };

        return $"{nextState}: {GlobalTimerService.FormatTimeSpan(nextDuration)}";
    }

    private string GetPomodoroStatusLabel() => TimerService.StatusLabel;

    private int GetCompletedInCurrentCycle()
    {
        if (TimerService.IntervalsBeforeLongBreak <= 0)
        {
            return 0;
        }

        var count = TimerService.CompletedWorkIntervalsCount % TimerService.IntervalsBeforeLongBreak;
        if (count == 0 && TimerService.CompletedWorkIntervalsCount > 0 && TimerService.CurrentPomodoroState == PomodoroState.LongBreak)
        {
            return TimerService.IntervalsBeforeLongBreak;
        }
        return count;
    }

    private async Task StopAndLog()
    {
        var elapsed = TimerService.Stop();
        if (elapsed > TimeSpan.Zero)
        {
            await OnLogSaved.InvokeAsync(elapsed);
        }
        TimerService.ClearTargetAndFocus();
        _sessionTargetText = string.Empty;
        _lastSyncedSessionTarget = (null, null, null);
        _focusDurationText = string.Empty;
        _lastSyncedFocusAlertAfter = null;
        _focusDurationFieldInitialized = true;
        _wasRunning = false;
    }

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
    }

    private void OnMobileHeaderKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is " " or "Enter")
        {
            ToggleExpanded();
        }
    }

    private string GetNextPomodoroTitle() => $"Next Pomodoro: {GetNextPomodoroLabelAndDuration()}";

    private string GetCompletedPomodoroTitle() =>
        $"Completed: {TimerService.CompletedWorkIntervalsCount} total, {GetCompletedInCurrentCycle()} of {TimerService.IntervalsBeforeLongBreak} in current cycle";

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        TimerService.Ticked -= OnTimerTicked;
    }
}
