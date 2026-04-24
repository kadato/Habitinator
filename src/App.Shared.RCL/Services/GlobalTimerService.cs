namespace App.Shared.RCL.Services;

public sealed class GlobalTimerService
{
    private readonly IClock _clock;
    private DateTimeOffset? _runningSince;
    private TimeSpan _accumulated = TimeSpan.Zero;

    public GlobalTimerService(IClock clock)
    {
        _clock = clock;
    }

    public string? TargetType { get; private set; }

    public string? TargetId { get; private set; }

    /// <summary>When the target is a board row, the item id; otherwise null (e.g. free-text session).</summary>
    public Guid? BoardItemId { get; private set; }

    public bool IsRunning => _runningSince.HasValue;

    public TimeSpan Elapsed =>
        _runningSince.HasValue
            ? _accumulated + (_clock.UtcNow - _runningSince.Value)
            : _accumulated;

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
        if (IsRunning)
        {
            return;
        }

        _runningSince = _clock.UtcNow;
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
        Pause();
        TimeSpan duration = _accumulated;
        _accumulated = TimeSpan.Zero;
        _runningSince = null;
        return duration;
    }

    public void RestoreFromPersistedStart(DateTimeOffset persistedStartUtc)
    {
        _runningSince = persistedStartUtc;
    }
}
