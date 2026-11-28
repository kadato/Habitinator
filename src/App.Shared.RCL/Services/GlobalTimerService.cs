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

    /// <summary>Guards every mutable field below. Raise events after releasing the lock.</summary>
    private readonly Lock _sync = new();

    private readonly Lock _heartbeatSync = new();

    private TimeSpan _accumulated = TimeSpan.Zero;

    /// <summary>Total <see cref="Elapsed" /> at which the next "time's up" event fires, when focus duration is set.</summary>
    private TimeSpan? _nextFocusMilestoneAtElapsed;

    private DateTimeOffset? _runningSince;
    private bool _pomodoroModeEnabled;
    private PomodoroState _currentPomodoroState = PomodoroState.Idle;
    private int _completedWorkIntervalsCount;
    private TimeSpan? _focusAlertAfter;
    private string? _targetType;
    private string? _targetId;
    private Guid? _boardItemId;
    private bool _awaitingFocusTimeUpPrompt;
    private TimeSpan _workDuration = TimeSpan.FromMinutes(25);
    private TimeSpan _shortBreakDuration = TimeSpan.FromMinutes(5);
    private TimeSpan _longBreakDuration = TimeSpan.FromMinutes(15);
    private int _intervalsBeforeLongBreak = 4;
    private CancellationTokenSource? _heartbeatCts;
    private bool _disposed;

    public bool PomodoroModeEnabled
    {
        get
        {
            lock (_sync)
            {
                return _pomodoroModeEnabled;
            }
        }
        set
        {
            lock (_sync)
            {
                if (_pomodoroModeEnabled == value)
                {
                    return;
                }

                _pomodoroModeEnabled = value;
                if (value)
                {
                    ResetCore();
                    _currentPomodoroState = PomodoroState.Idle;
                    _completedWorkIntervalsCount = 0;
                    SetFocusAlertAfterCore(null);
                }
                else
                {
                    ResetCore();
                }
            }

            StopHeartbeat();
            Ticked?.Invoke();
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

    public PomodoroState CurrentPomodoroState
    {
        get
        {
            lock (_sync)
            {
                return _currentPomodoroState;
            }
        }
    }

    public int CompletedWorkIntervalsCount
    {
        get
        {
            lock (_sync)
            {
                return _completedWorkIntervalsCount;
            }
        }
    }

    // Configurable durations populated from UserPreferences in the UI component
    public TimeSpan WorkDuration
    {
        get
        {
            lock (_sync)
            {
                return _workDuration;
            }
        }
        set
        {
            lock (_sync)
            {
                _workDuration = value;
            }
        }
    }

    public TimeSpan ShortBreakDuration
    {
        get
        {
            lock (_sync)
            {
                return _shortBreakDuration;
            }
        }
        set
        {
            lock (_sync)
            {
                _shortBreakDuration = value;
            }
        }
    }

    public TimeSpan LongBreakDuration
    {
        get
        {
            lock (_sync)
            {
                return _longBreakDuration;
            }
        }
        set
        {
            lock (_sync)
            {
                _longBreakDuration = value;
            }
        }
    }

    public int IntervalsBeforeLongBreak
    {
        get
        {
            lock (_sync)
            {
                return _intervalsBeforeLongBreak;
            }
        }
        set
        {
            lock (_sync)
            {
                _intervalsBeforeLongBreak = Math.Max(1, value);
            }
        }
    }

    public string? TargetType
    {
        get
        {
            lock (_sync)
            {
                return _targetType;
            }
        }
    }

    public string? TargetId
    {
        get
        {
            lock (_sync)
            {
                return _targetId;
            }
        }
    }

    /// <summary>When the target is a board row, the item id. Otherwise null, as in a free-text session.</summary>
    public Guid? BoardItemId
    {
        get
        {
            lock (_sync)
            {
                return _boardItemId;
            }
        }
    }

    /// <summary>
    ///     Optional total elapsed length at which a "time's up" event may fire until <see cref="Stop" />.
    ///     <see langword="null" /> or non-positive: no automatic end alert, stopwatch only.
    /// </summary>
    public TimeSpan? FocusAlertAfter
    {
        get
        {
            lock (_sync)
            {
                return _focusAlertAfter;
            }
        }
        set
        {
            lock (_sync)
            {
                if (_focusAlertAfter == value)
                {
                    return;
                }

                _focusAlertAfter = value;
                RearmFocusMilestoneCore();
            }

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
    public bool AwaitingFocusTimeUpPrompt
    {
        get
        {
            lock (_sync)
            {
                return _awaitingFocusTimeUpPrompt;
            }
        }
    }

    public TimeSpan Elapsed
    {
        get
        {
            lock (_sync)
            {
                return ElapsedCore();
            }
        }
    }

    /// <summary>
    ///     Captures the current session for the local store. The snapshot holds the
    ///     live elapsed time, so call this before the app suspends or closes.
    /// </summary>
    public TimerSessionSnapshot CaptureState()
    {
        lock (_sync)
        {
            return new TimerSessionSnapshot(
                ElapsedCore().Ticks,
                _targetType,
                _targetId,
                _boardItemId,
                _focusAlertAfter?.Ticks,
                _clock.UtcNow,
                _pomodoroModeEnabled,
                _currentPomodoroState.ToString(),
                _completedWorkIntervalsCount);
        }
    }

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

        lock (_sync)
        {
            if (_runningSince.HasValue)
            {
                return false;
            }

            _accumulated = TimeSpan.FromTicks(Math.Max(0, snapshot.ElapsedTicks));
            _runningSince = null;
            _nextFocusMilestoneAtElapsed = null;

            // Bypass the mode setter. It resets the session, which would wipe the
            // elapsed time restored above.
            _pomodoroModeEnabled = snapshot.PomodoroModeEnabled;
            _currentPomodoroState = Enum.TryParse<PomodoroState>(snapshot.PomodoroStateName, out var state)
                ? state
                : PomodoroState.Idle;
            _completedWorkIntervalsCount = Math.Max(0, snapshot.CompletedIntervals);

            if (snapshot.TargetType is null or "Session")
            {
                _targetType = string.IsNullOrWhiteSpace(snapshot.TargetId) ? null : "Session";
                _targetId = string.IsNullOrWhiteSpace(snapshot.TargetId) ? null : snapshot.TargetId.Trim();
                _boardItemId = null;
            }
            else if (snapshot.TargetId is not null)
            {
                _targetType = snapshot.TargetType;
                _targetId = snapshot.TargetId;
                _boardItemId = snapshot.BoardItemId;
            }

            SetFocusAlertAfterCore(
                snapshot.FocusAlertAfterTicks is { } ticks && ticks > 0
                    ? TimeSpan.FromTicks(ticks)
                    : null);
        }

        NotifyStateChanged();
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
        lock (_sync)
        {
            if (!_runningSince.HasValue || _awaitingFocusTimeUpPrompt)
            {
                return false;
            }

            if (_focusAlertAfter is not { } alert || alert <= TimeSpan.Zero)
            {
                return false;
            }

            return _nextFocusMilestoneAtElapsed is { } next && ElapsedCore() >= next;
        }
    }

    public void SelectTarget(string targetType, string targetId, Guid? boardItemId = null)
    {
        lock (_sync)
        {
            // Don't change the target while the timer is running. Keep the session target.
            if (_runningSince.HasValue)
            {
                return;
            }

            _targetType = targetType;
            _targetId = targetId;
            _boardItemId = boardItemId;
        }

        NotifyStateChanged();
    }

    /// <summary>Sets a user-entered focus label, or clears the target when the label is empty.</summary>
    public void SetManualTarget(string? label)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                _targetType = null;
                _targetId = null;
                _boardItemId = null;
            }
            else
            {
                _targetType = "Session";
                _targetId = label.Trim();
                _boardItemId = null;
            }
        }

        NotifyStateChanged();
    }

    public event Action? Ticked;

    private void StartHeartbeat()
    {
        CancellationToken token;
        lock (_heartbeatSync)
        {
            if (_heartbeatCts is not null || _disposed)
            {
                return;
            }

            _heartbeatCts = new CancellationTokenSource();
            token = _heartbeatCts.Token;
        }

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
        CancellationTokenSource? cts;
        lock (_heartbeatSync)
        {
            cts = _heartbeatCts;
            _heartbeatCts = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_runningSince.HasValue)
            {
                return;
            }

            if (_awaitingFocusTimeUpPrompt)
            {
                _awaitingFocusTimeUpPrompt = false;
                _nextFocusMilestoneAtElapsed = null;
                _runningSince = _clock.UtcNow;
            }
            else
            {
                if (_pomodoroModeEnabled && _currentPomodoroState == PomodoroState.Idle)
                {
                    _currentPomodoroState = PomodoroState.Work;
                    SetFocusAlertAfterCore(_workDuration);
                }

                if (_nextFocusMilestoneAtElapsed is null
                    && _focusAlertAfter is { } alert
                    && alert > TimeSpan.Zero)
                {
                    _nextFocusMilestoneAtElapsed = _accumulated + alert;
                }

                _runningSince = _clock.UtcNow;
            }
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
        lock (_sync)
        {
            if (!_awaitingFocusTimeUpPrompt)
            {
                return;
            }

            _awaitingFocusTimeUpPrompt = false;
            _nextFocusMilestoneAtElapsed = null;
            _runningSince ??= _clock.UtcNow;
        }

        StartHeartbeat();
        Ticked?.Invoke();
        NotifyStateChanged();
    }

    /// <summary>
    ///     Enters the focus <c>time's up</c> prompt state. The stopwatch keeps running. Call <see cref="Stop" /> to log,
    ///     or <see cref="ResumeAfterFocusPromptNotDone" /> after "not done".
    /// </summary>
    public void PauseForFocusTimeUp()
    {
        lock (_sync)
        {
            if (!_runningSince.HasValue)
            {
                return;
            }

            _awaitingFocusTimeUpPrompt = true;
        }

        NotifyStateChanged();
    }

    public void Pause()
    {
        StopHeartbeat();
        lock (_sync)
        {
            if (!PauseCore())
            {
                return;
            }
        }

        Ticked?.Invoke();
        NotifyStateChanged();
    }

    public TimeSpan Stop()
    {
        StopHeartbeat();
        TimeSpan duration;
        lock (_sync)
        {
            PauseCore();
            _awaitingFocusTimeUpPrompt = false;
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
        lock (_sync)
        {
            ResetCore();
        }

        Ticked?.Invoke();
        NotifyStateChanged();
    }

    /// <summary>
    ///     Clears the target and focus alert settings. Call after logging a completed session.
    /// </summary>
    public void ClearTargetAndFocus()
    {
        lock (_sync)
        {
            if (_focusAlertAfter is not null)
            {
                SetFocusAlertAfterCore(null);
            }

            _nextFocusMilestoneAtElapsed = null;
            if (!_pomodoroModeEnabled)
            {
                _targetType = null;
                _targetId = null;
                _boardItemId = null;
            }
        }

        NotifyStateChanged();
    }

    public void IncrementCompletedIntervals()
    {
        lock (_sync)
        {
            _completedWorkIntervalsCount++;
        }
    }

    public void TransitionToBreak()
    {
        lock (_sync)
        {
            var cycleNum = _completedWorkIntervalsCount;
            if (cycleNum > 0 && cycleNum % _intervalsBeforeLongBreak == 0)
            {
                _currentPomodoroState = PomodoroState.LongBreak;
                SetFocusAlertAfterCore(_longBreakDuration);
            }
            else
            {
                _currentPomodoroState = PomodoroState.ShortBreak;
                SetFocusAlertAfterCore(_shortBreakDuration);
            }

            ResetCore();
        }

        StopHeartbeat();
        Ticked?.Invoke();
        NotifyStateChanged();
    }

    public void TransitionToWork()
    {
        lock (_sync)
        {
            _currentPomodoroState = PomodoroState.Work;
            SetFocusAlertAfterCore(_workDuration);
            ResetCore();
        }

        StopHeartbeat();
        Ticked?.Invoke();
        NotifyStateChanged();
    }

    public void ResetPomodoroSession()
    {
        lock (_sync)
        {
            ResetCore();
            _currentPomodoroState = PomodoroState.Idle;
            _completedWorkIntervalsCount = 0;
            SetFocusAlertAfterCore(null);
        }

        StopHeartbeat();
        Ticked?.Invoke();
        NotifyStateChanged();
    }

    public void ResetSession()
    {
        lock (_sync)
        {
            if (_pomodoroModeEnabled)
            {
                ResetCore();
                _currentPomodoroState = PomodoroState.Idle;
                _completedWorkIntervalsCount = 0;
                SetFocusAlertAfterCore(null);
            }
            else
            {
                ResetCore();
            }
        }

        StopHeartbeat();
        Ticked?.Invoke();
        NotifyStateChanged();
    }

    /// <summary>Rearms the milestone from the current elapsed time. Caller must hold <see cref="_sync" />.</summary>
    private void SetFocusAlertAfterCore(TimeSpan? value)
    {
        _focusAlertAfter = value;
        RearmFocusMilestoneCore();
    }

    /// <summary>Rearms the milestone from the current elapsed time. Caller must hold <see cref="_sync" />.</summary>
    private void RearmFocusMilestoneCore()
    {
        _nextFocusMilestoneAtElapsed = _focusAlertAfter is { } alert && alert > TimeSpan.Zero
            ? ElapsedCore() + alert
            : null;
    }

    /// <summary>Computes the live elapsed time. Caller must hold <see cref="_sync" />.</summary>
    private TimeSpan ElapsedCore() =>
        _runningSince.HasValue
            ? _accumulated + (_clock.UtcNow - _runningSince.Value)
            : _accumulated;

    /// <summary>Accumulates the running span and pauses. Caller must hold <see cref="_sync" />.</summary>
    /// <returns>True when a running session was paused.</returns>
    private bool PauseCore()
    {
        if (_runningSince is not { } started)
        {
            return false;
        }

        _accumulated += _clock.UtcNow - started;
        _runningSince = null;
        return true;
    }

    /// <summary>Pauses and clears the session. Caller must hold <see cref="_sync" />.</summary>
    private void ResetCore()
    {
        PauseCore();
        _awaitingFocusTimeUpPrompt = false;
        _accumulated = TimeSpan.Zero;
        _runningSince = null;
        _nextFocusMilestoneAtElapsed = null;
    }

    public string GetDisplayTime()
    {
        lock (_sync)
        {
            if (_pomodoroModeEnabled)
            {
                if (_currentPomodoroState == PomodoroState.Idle)
                {
                    return FormatTimeSpan(_workDuration);
                }

                if (_focusAlertAfter.HasValue)
                {
                    var remaining = _focusAlertAfter.Value - ElapsedCore();
                    if (remaining < TimeSpan.Zero)
                    {
                        remaining = TimeSpan.Zero;
                    }
                    return FormatTimeSpan(remaining);
                }
            }
            return FormatTimeSpan(ElapsedCore());
        }
    }

    public string StatusLabel
    {
        get
        {
            lock (_sync)
            {
                if (_pomodoroModeEnabled)
                {
                    return _currentPomodoroState switch
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
        lock (_heartbeatSync)
        {
            _disposed = true;
        }

        StopHeartbeat();
    }
}
