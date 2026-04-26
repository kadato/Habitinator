namespace App.Shared.RCL.Services;

public sealed class GlobalTimerService
{
    private readonly IClock _clock;
    private TimeSpan _accumulated = TimeSpan.Zero;

    private TimeSpan? _focusAlertAfter;

    /// <summary>Total <see cref="Elapsed" /> at which the next "time's up" event fires, when focus duration is set.</summary>
    private TimeSpan? _nextFocusMilestoneAtElapsed;

    private DateTimeOffset? _runningSince;

    public GlobalTimerService(IClock clock)
    {
        _clock = clock;
    }

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
        get => _focusAlertAfter;
        set
        {
            if (_focusAlertAfter == value) return;

            _focusAlertAfter = value;
            RearmFocusMilestone();
        }
    }

    public bool IsRunning => _runningSince.HasValue;

    /// <summary>
    ///     The timer is paused for a "time's up" dialog; the user must log, pick not done, or (if misrouted) use
    ///     <see cref="Start" />
    ///     which will resume the same as not done.
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
        if (!IsRunning) return false;

        if (!_focusAlertAfter.HasValue || _focusAlertAfter <= TimeSpan.Zero) return false;

        if (_nextFocusMilestoneAtElapsed is null) return false;

        return Elapsed >= _nextFocusMilestoneAtElapsed;
    }

    public void SelectTarget(string targetType, string targetId, Guid? boardItemId = null)
    {
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
        if (IsRunning) return;

        if (AwaitingFocusTimeUpPrompt)
        {
            ResumeAfterFocusPromptNotDone();
            return;
        }

        if (_nextFocusMilestoneAtElapsed is null
            && _focusAlertAfter is { } f
            && f > TimeSpan.Zero)
            _nextFocusMilestoneAtElapsed = _accumulated + f;

        _runningSince = _clock.UtcNow;
    }

    /// <summary>
    ///     Resumes the stopwatch after a "not done" result on a focus <c>time's up</c> prompt, arming the next
    ///     milestone at one more <see cref="FocusAlertAfter" /> interval from the paused total.
    /// </summary>
    public void ResumeAfterFocusPromptNotDone()
    {
        if (IsRunning) return;

        if (!AwaitingFocusTimeUpPrompt) return;

        AwaitingFocusTimeUpPrompt = false;
        if (_focusAlertAfter is { } f && f > TimeSpan.Zero) _nextFocusMilestoneAtElapsed = _accumulated + f;

        _runningSince = _clock.UtcNow;
    }

    /// <summary>
    ///     Stops the clock at the end of a focus block; call <see cref="Stop" /> to log, or
    ///     <see cref="ResumeAfterFocusPromptNotDone" /> after "not done".
    /// </summary>
    public void PauseForFocusTimeUp()
    {
        Pause();
        AwaitingFocusTimeUpPrompt = true;
    }

    public void Pause()
    {
        if (!IsRunning) return;

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

    public void RestoreFromPersistedStart(DateTimeOffset persistedStartUtc)
    {
        _runningSince = persistedStartUtc;
    }

    private void RearmFocusMilestone()
    {
        if (!_focusAlertAfter.HasValue || _focusAlertAfter <= TimeSpan.Zero)
        {
            _nextFocusMilestoneAtElapsed = null;
            return;
        }

        if (IsRunning)
            _nextFocusMilestoneAtElapsed = Elapsed + _focusAlertAfter.Value;
        else
            _nextFocusMilestoneAtElapsed = _accumulated + _focusAlertAfter.Value;
    }
}
