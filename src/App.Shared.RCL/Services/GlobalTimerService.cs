using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

public enum PomodoroState
{
    Idle,
    Work,
    ShortBreak,
    LongBreak
}

public sealed class GlobalTimerService(IClock clock, ILogger<GlobalTimerService>? logger = null) : IDisposable
{
    private readonly IClock _clock = clock;
    private readonly ILogger<GlobalTimerService>? _logger = logger;
    private readonly Lock _sync = new();
    private TimeSpan _accumulated = TimeSpan.Zero;

    /// <summary>Total <see cref="Elapsed" /> at which the next "time's up" event fires, when focus duration is set.</summary>
    private TimeSpan? _nextFocusMilestoneAtElapsed;

    private DateTimeOffset? _runningSince;

    private bool _pomodoroModeEnabled;

    public bool PomodoroModeEnabled
    {
        get => _pomodoroModeEnabled;
        set
        {
            if (_pomodoroModeEnabled == value)
            {
                return;
            }

            _pomodoroModeEnabled = value;
            if (value)
            {
                ResetPomodoroSession();
            }
            else
            {
                Reset();
            }

            NotifyStateChanged();
        }
    }

    public event Action? StateChanged;

    public void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }

    public void TogglePomodoroMode()
    {
        PomodoroModeEnabled = !PomodoroModeEnabled;
    }

    public PomodoroState CurrentPomodoroState { get; private set; } = PomodoroState.Idle;

    public int CompletedWorkIntervalsCount { get; private set; }

    // Configurable durations populated from UserPreferences in the UI component
    public TimeSpan WorkDuration { get; set; } = TimeSpan.FromMinutes(25);

    public TimeSpan ShortBreakDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan LongBreakDuration { get; set; } = TimeSpan.FromMinutes(15);

    public int IntervalsBeforeLongBreak { get; set; } = 4;

    public string? TargetType { get; private set; }

    public string? TargetId { get; private set; }

    /// <summary>When the target is a board row, the item id. Otherwise null, as in a free-text session.</summary>
    public Guid? BoardItemId { get; private set; }

    /// <summary>
    ///     Optional total elapsed length at which a "time's up" event may fire until <see cref="Stop" />.
    ///     <see langword="null" /> or non-positive: no automatic end alert, stopwatch only.
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
            NotifyStateChanged();
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _runningSince.HasValue;
            }
        }
    }

    /// <summary>
    ///     A "time's up" dialog is showing. The stopwatch keeps running. The user must log, pick not done, or, if
    ///     misrouted, use <see cref="Start" /> which dismisses the prompt the same as not done.
    /// </summary>
    public bool AwaitingFocusTimeUpPrompt { get; private set; }

    public TimeSpan Elapsed
    {
        get
        {
            lock (_sync)
            {
                return _runningSince.HasValue
                    ? _accumulated + (_clock.UtcNow - _runningSince.Value)
                    : _accumulated;
            }
        }
    }

    /// <summary>
    ///     Captures the current session for the local store. The snapshot holds the
    ///     live elapsed time, so call this before the app suspends or closes.
    /// </summary>
    public TimerSessionSnapshot CaptureState() =>
        new(
            Elapsed.Ticks,
            TargetType,
            TargetId,
            BoardItemId,
            FocusAlertAfter?.Ticks,
            _clock.UtcNow,
            PomodoroModeEnabled,
            CurrentPomodoroState.ToString(),
            CompletedWorkIntervalsCount);

    /// <summary>
    ///     Restores a saved session paused. A running timer never resumes on its own
    ///     after a restart. The user starts it again with one tap.
    /// </summary>
    /// <returns>True when the snapshot held a session worth restoring.</returns>
    public bool RestoreState(TimerSessionSnapshot? snapshot)
    {
        if (snapshot is null || IsEmptySnapshot(snapshot))
        {
            return false;
        }

        if (IsRunning)
        {
            return false;
        }

        lock (_sync)
        {
            _accumulated = TimeSpan.FromTicks(Math.Max(0, snapshot.ElapsedTicks));
            _runningSince = null;
            _nextFocusMilestoneAtElapsed = null;
        }

        // Bypass the mode setter. It resets the session, which would wipe the
        // elapsed time restored above.
        _pomodoroModeEnabled = snapshot.PomodoroModeEnabled;
        CurrentPomodoroState = Enum.TryParse<PomodoroState>(snapshot.PomodoroStateName, out var state)
            ? state
            : PomodoroState.Idle;
        CompletedWorkIntervalsCount = Math.Max(0, snapshot.CompletedIntervals);

        if (snapshot.TargetType is null or "Session")
        {
            SetManualTarget(snapshot.TargetId);
        }
        else if (snapshot.TargetId is not null)
        {
            SelectTarget(snapshot.TargetType, snapshot.TargetId, snapshot.BoardItemId);
        }

        FocusAlertAfter = snapshot.FocusAlertAfterTicks is { } ticks && ticks > 0
            ? TimeSpan.FromTicks(ticks)
            : null;
        return true;
    }

    private static bool IsEmptySnapshot(TimerSessionSnapshot snapshot) =>
        snapshot.ElapsedTicks <= 0
        && snapshot.TargetId is null
        && snapshot.FocusAlertAfterTicks is null
        && snapshot.CompletedIntervals <= 0
        && (snapshot.PomodoroStateName is null || snapshot.PomodoroStateName == nameof(PomodoroState.Idle));

    /// <summary>
    ///     Returns <see langword="true" /> when the timer is <see cref="IsRunning">running</see>,
    ///     a positive <see cref="FocusAlertAfter" /> is set, and <see cref="Elapsed" /> has reached the next milestone.
    ///     The caller should <see cref="PauseForFocusTimeUp" /> and show a prompt, then
    ///     either <see cref="Stop" /> to log or <see cref="ResumeAfterFocusPromptNotDone" /> for not done.
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
        NotifyStateChanged();
    }

    /// <summary>Sets a user-entered focus label, or clears the target when the label is empty.</summary>
    public void SetManualTarget(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            TargetType = null;
            TargetId = null;
            BoardItemId = null;
            NotifyStateChanged();
            return;
        }

        TargetType = "Session";
        TargetId = label.Trim();
        BoardItemId = null;
        NotifyStateChanged();
    }

    public event Action? Ticked;
    private CancellationTokenSource? _heartbeatCts;

    private void StartHeartbeat()
    {
        if (_heartbeatCts is not null)
        {
            return;
        }

        _heartbeatCts = new CancellationTokenSource();
        var token = _heartbeatCts.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    try
                    {
                        Ticked?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        // One bad subscriber must not kill the heartbeat for the rest of the session.
                        _logger?.LogWarning(ex, "A timer tick subscriber threw.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Heartbeat cancelled normally when timer stopped or paused.
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Timer heartbeat stopped unexpectedly.");
            }
        }, token);
    }

    private void StopHeartbeat()
    {
        if (_heartbeatCts is null)
        {
            return;
        }

        _heartbeatCts.Cancel();
        _heartbeatCts.Dispose();
        _heartbeatCts = null;
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
            lock (_sync)
            {
                _nextFocusMilestoneAtElapsed ??= _accumulated + f;
            }
        }

        lock (_sync)
        {
            _runningSince = _clock.UtcNow;
        }

        StartHeartbeat();
        Ticked?.Invoke();
        NotifyStateChanged();
    }

    /// <summary>
    ///     Dismisses a focus <c>time's up</c> prompt after "not done". The stopwatch keeps running. No further
    ///     time's up alerts fire until <see cref="Stop" /> or <see cref="Reset" /> and a new session starts.
    /// </summary>
    public void ResumeAfterFocusPromptNotDone()
    {
        if (!AwaitingFocusTimeUpPrompt)
        {
            return;
        }

        AwaitingFocusTimeUpPrompt = false;
        lock (_sync)
        {
            _nextFocusMilestoneAtElapsed = null;
        }

        if (!IsRunning)
        {
            _runningSince = _clock.UtcNow;
            StartHeartbeat();
        }

        Ticked?.Invoke();
        NotifyStateChanged();
    }

    /// <summary>
    ///     Enters the focus <c>time's up</c> prompt state. The stopwatch keeps running. Call <see cref="Stop" /> to log,
    ///     or <see cref="ResumeAfterFocusPromptNotDone" /> after "not done".
    /// </summary>
    public void PauseForFocusTimeUp()
    {
        if (!IsRunning)
        {
            return;
        }

        AwaitingFocusTimeUpPrompt = true;
        NotifyStateChanged();
    }

    public void Pause()
    {
        StopHeartbeat();
        DateTimeOffset? runningSince;
        lock (_sync)
        {
            runningSince = _runningSince;
        }

        if (runningSince is not { } started)
        {
            return;
        }

        lock (_sync)
        {
            // Re-check under lock so a concurrent Start cannot lose time.
            if (_runningSince is not { } current || current != started)
            {
                return;
            }

            _accumulated += _clock.UtcNow - current;
            _runningSince = null;
        }

        Ticked?.Invoke();
        NotifyStateChanged();
    }

    public TimeSpan Stop()
    {
        StopHeartbeat();
        AwaitingFocusTimeUpPrompt = false;
        Pause();
        TimeSpan duration;
        lock (_sync)
        {
            duration = _accumulated;
            _accumulated = TimeSpan.Zero;
            _runningSince = null;
            _nextFocusMilestoneAtElapsed = null;
        }

        Ticked?.Invoke();
        NotifyStateChanged();
        return duration;
    }

    /// <summary>
    ///     Resets the timer without logging. Clears elapsed time but preserves target.
    /// </summary>
    public void Reset()
    {
        StopHeartbeat();
        AwaitingFocusTimeUpPrompt = false;
        Pause();
        lock (_sync)
        {
            _accumulated = TimeSpan.Zero;
            _runningSince = null;
            _nextFocusMilestoneAtElapsed = null;
        }

        Ticked?.Invoke();
        NotifyStateChanged();
    }

    /// <summary>
    ///     Clears the target and focus alert settings. Call after logging a completed session.
    /// </summary>
    public void ClearTargetAndFocus()
    {
        FocusAlertAfter = null;
        lock (_sync)
        {
            _nextFocusMilestoneAtElapsed = null;
        }
        if (!PomodoroModeEnabled)
        {
            TargetType = null;
            TargetId = null;
            BoardItemId = null;
        }
        NotifyStateChanged();
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

    public void ResetSession()
    {
        if (PomodoroModeEnabled)
        {
            ResetPomodoroSession();
        }
        else
        {
            Reset();
        }
    }

    private void RearmFocusMilestone()
    {
        if (!FocusAlertAfter.HasValue || FocusAlertAfter <= TimeSpan.Zero)
        {
            lock (_sync)
            {
                _nextFocusMilestoneAtElapsed = null;
            }

            return;
        }

        var elapsed = Elapsed;
        lock (_sync)
        {
            _nextFocusMilestoneAtElapsed = elapsed + FocusAlertAfter.Value;
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

    public string StatusLabel
    {
        get
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
    }

    public static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return ts.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        }
        return ts.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        StopHeartbeat();
    }
}
