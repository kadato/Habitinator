namespace App.Shared.RCL.Services;

public enum PomodoroState
{
    Idle,
    Work,
    ShortBreak,
    LongBreak
}

public sealed class GlobalTimerService(IClock clock)
{
    private readonly IClock _clock = clock;
    private TimeSpan _accumulated = TimeSpan.Zero;

    /// <summary>Total <see cref="Elapsed" /> at which the next "time's up" event fires, when focus duration is set.</summary>
    private TimeSpan? _nextFocusMilestoneAtElapsed;

    private DateTimeOffset? _runningSince;

    public bool PomodoroModeEnabled { get; set; }

    public PomodoroState CurrentPomodoroState { get; private set; } = PomodoroState.Idle;

    public int CompletedWorkIntervalsCount { get; private set; }

    // Configurable durations populated from UserPreferences (in UI component)
    public TimeSpan WorkDuration { get; set; } = TimeSpan.FromMinutes(25);

    public TimeSpan ShortBreakDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan LongBreakDuration { get; set; } = TimeSpan.FromMinutes(15);

    public int IntervalsBeforeLongBreak { get; set; } = 4;

    public string? TargetType { get; private set; }

    public string? TargetId { get; private set; }

    /// <summary>When the target is a board row, the item id; otherwise null (e.g. free-text session).</summary>
    public Guid? BoardItemId { get; private set; }

    /// <summary>
    ///     Optional total elapsed length at which a "time's up" event may fire until <see cref="Stop" />.
    ///     <see langword="null" /> or non-positive: no automatic end alert (stopwatch only).
    /// </summary>
    public TimeSpan? FocusAlertAfter
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            RearmFocusMilestone();
        }
    }

    public bool IsRunning => _runningSince.HasValue;

    /// <summary>
    ///     A "time's up" dialog is showing; the stopwatch keeps running. The user must log, pick not done, or (if
    ///     misrouted) use <see cref="Start" /> which dismisses the prompt the same as not done.
    /// </summary>
    public bool AwaitingFocusTimeUpPrompt { get; private set; }

    public TimeSpan Elapsed =>
        _runningSince.HasValue
            ? _accumulated + (_clock.UtcNow - _runningSince.Value)
            : _accumulated;

    /// <summary>
    ///     Returns <see langword="true" /> when the timer is <see cref="IsRunning">running</see>,
    ///     a positive <see cref="FocusAlertAfter" /> is set, and <see cref="Elapsed" /> has reached the next milestone.
    ///     The caller should <see cref="PauseForFocusTimeUp" /> and show a prompt, then
    ///     either <see cref="Stop" /> (log) or <see cref="ResumeAfterFocusPromptNotDone" /> (not done).
    /// </summary>
    public bool TryConsumeFocusDurationReached()
    {
        if (!IsRunning)
        {
            return false;
        }

        if (AwaitingFocusTimeUpPrompt)
        {
            return false;
        }

        if (!FocusAlertAfter.HasValue || FocusAlertAfter <= TimeSpan.Zero)
        {
            return false;
        }

        if (_nextFocusMilestoneAtElapsed is null)
        {
            return false;
        }

        return Elapsed >= _nextFocusMilestoneAtElapsed;
    }

    public void SelectTarget(string targetType, string targetId, Guid? boardItemId = null)
    {
        // Don't change target while timer is running - preserve the session target
        if (IsRunning)
        {
            return;
        }

        TargetType = targetType;
        TargetId = targetId;
        BoardItemId = boardItemId;
    }

    /// <summary>Sets a user-entered focus label, or clears the target when the label is empty.</summary>
    public void SetManualTarget(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            TargetType = null;
            TargetId = null;
            BoardItemId = null;
            return;
        }

        TargetType = "Session";
        TargetId = label.Trim();
        BoardItemId = null;
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        if (AwaitingFocusTimeUpPrompt)
        {
            ResumeAfterFocusPromptNotDone();
            return;
        }

        if (PomodoroModeEnabled && CurrentPomodoroState == PomodoroState.Idle)
        {
            CurrentPomodoroState = PomodoroState.Work;
            FocusAlertAfter = WorkDuration;
        }

        if (_nextFocusMilestoneAtElapsed is null
            && FocusAlertAfter is { } f
            && f > TimeSpan.Zero)
        {
            _nextFocusMilestoneAtElapsed = _accumulated + f;
        }

        _runningSince = _clock.UtcNow;
    }

    /// <summary>
    ///     Dismisses a focus <c>time's up</c> prompt after "not done". The stopwatch keeps running; no further
    ///     time's up alerts fire until <see cref="Stop" /> or <see cref="Reset" /> and a new session starts.
    /// </summary>
    public void ResumeAfterFocusPromptNotDone()
    {
        if (!AwaitingFocusTimeUpPrompt)
        {
            return;
        }

        AwaitingFocusTimeUpPrompt = false;
        _nextFocusMilestoneAtElapsed = null;

        if (!IsRunning)
        {
            _runningSince = _clock.UtcNow;
        }
    }

    /// <summary>
    ///     Enters the focus <c>time's up</c> prompt state. The stopwatch keeps running; call <see cref="Stop" /> to log,
    ///     or <see cref="ResumeAfterFocusPromptNotDone" /> after "not done".
    /// </summary>
    public void PauseForFocusTimeUp()
    {
        if (!IsRunning)
        {
            return;
        }

        AwaitingFocusTimeUpPrompt = true;
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _accumulated += _clock.UtcNow - _runningSince!.Value;
        _runningSince = null;
    }

    public TimeSpan Stop()
    {
        AwaitingFocusTimeUpPrompt = false;
        Pause();
        var duration = _accumulated;
        _accumulated = TimeSpan.Zero;
        _runningSince = null;
        _nextFocusMilestoneAtElapsed = null;
        return duration;
    }

    /// <summary>
    ///     Resets the timer without logging. Clears elapsed time but preserves target.
    /// </summary>
    public void Reset()
    {
        AwaitingFocusTimeUpPrompt = false;
        Pause();
        _accumulated = TimeSpan.Zero;
        _runningSince = null;
        _nextFocusMilestoneAtElapsed = null;
    }

    /// <summary>
    ///     Clears the target and focus alert settings. Call after logging a completed session.
    /// </summary>
    public void ClearTargetAndFocus()
    {
        FocusAlertAfter = null;
        _nextFocusMilestoneAtElapsed = null;
        if (!PomodoroModeEnabled)
        {
            TargetType = null;
            TargetId = null;
            BoardItemId = null;
        }
    }

    public void IncrementCompletedIntervals()
    {
        CompletedWorkIntervalsCount++;
    }

    public void TransitionToBreak()
    {
        var cycleNum = CompletedWorkIntervalsCount;
        if (cycleNum > 0 && cycleNum % IntervalsBeforeLongBreak == 0)
        {
            CurrentPomodoroState = PomodoroState.LongBreak;
            FocusAlertAfter = LongBreakDuration;
        }
        else
        {
            CurrentPomodoroState = PomodoroState.ShortBreak;
            FocusAlertAfter = ShortBreakDuration;
        }
        Reset();
    }

    public void TransitionToWork()
    {
        CurrentPomodoroState = PomodoroState.Work;
        FocusAlertAfter = WorkDuration;
        Reset();
    }

    public void ResetPomodoroSession()
    {
        Reset();
        CurrentPomodoroState = PomodoroState.Idle;
        CompletedWorkIntervalsCount = 0;
        FocusAlertAfter = null;
    }

    public void RestoreFromPersistedStart(DateTimeOffset persistedStartUtc)
    {
        _runningSince = persistedStartUtc;
    }

    private void RearmFocusMilestone()
    {
        if (!FocusAlertAfter.HasValue || FocusAlertAfter <= TimeSpan.Zero)
        {
            _nextFocusMilestoneAtElapsed = null;
            return;
        }

        if (IsRunning)
        {
            _nextFocusMilestoneAtElapsed = Elapsed + FocusAlertAfter.Value;
        }
        else
        {
            _nextFocusMilestoneAtElapsed = _accumulated + FocusAlertAfter.Value;
        }
    }

    public string GetDisplayTime()
    {
        if (PomodoroModeEnabled)
        {
            if (CurrentPomodoroState == PomodoroState.Idle)
            {
                return FormatTimeSpan(WorkDuration);
            }

            if (FocusAlertAfter.HasValue)
            {
                var remaining = FocusAlertAfter.Value - Elapsed;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }
                return FormatTimeSpan(remaining);
            }
        }
        return FormatTimeSpan(Elapsed);
    }

    public string GetStatusLabel()
    {
        if (PomodoroModeEnabled)
        {
            return CurrentPomodoroState switch
            {
                PomodoroState.Work => "Focusing",
                PomodoroState.ShortBreak => "Short Break",
                PomodoroState.LongBreak => "Long Break",
                _ => "Get Ready"
            };
        }
        return "Focusing";
    }

    public static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return ts.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        }
        return ts.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}
