namespace App.Shared.RCL.Services;

/// <summary>
/// Bridges the OS back gesture to Blazor overlays. The active layout registers a
/// handler; on MAUI the Android activity invokes it. On web the history shim in
/// <c>mobileBack.js</c> drives the same overlay order through
/// <c>ThemeLayoutBase.OnHistoryBackAsync</c>. Also owns the double-press-to-exit
/// window so every host shares one timing.
/// </summary>
public sealed class MobileBackHandler
{
    private Func<Task<bool>>? _handler;
    private DateTimeOffset? _lastBackAttempt;

    /// <summary>Second press within this window confirms exit.</summary>
    public static TimeSpan ExitConfirmWindow { get; } = TimeSpan.FromSeconds(2);

    public void SetHandler(Func<Task<bool>>? handler) => _handler = handler;

    /// <summary>
    /// Runs the registered handler. Returns false when nothing is registered so the
    /// caller can fall back to the platform default. Errors are consumed rather than
    /// letting a half-open overlay exit the app.
    /// </summary>
    public async Task<bool> RequestBackAsync()
    {
        var handler = _handler;
        if (handler is null)
        {
            return false;
        }

        try
        {
            return await handler().ConfigureAwait(false);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Returns true when this press confirms exit, a second press inside the window.</summary>
    public bool RegisterBackAttempt(DateTimeOffset now)
    {
        if (_lastBackAttempt.HasValue && now - _lastBackAttempt.Value <= ExitConfirmWindow)
        {
            _lastBackAttempt = null;
            return true;
        }

        _lastBackAttempt = now;
        return false;
    }

    public void ResetExitState() => _lastBackAttempt = null;
}
