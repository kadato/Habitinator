using App.Shared.RCL.Models;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

public sealed class UserDayService(
    IClock clock,
    IUserTimeZoneService timeZone,
    IUserPreferencesService preferences,
    ILogger<UserDayService> logger) : IUserDayService, IDisposable
{
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private TimeSpan _dayStartLocalTime;
    private DateOnly _lastSeenDay;
    private bool _initialized;
    private bool _disposed;

    public event EventHandler? DayChanged;

    public DateOnly Today => DailySchedule.LocalToday(clock, timeZone, _dayStartLocalTime);

    public TimeSpan DayStartLocalTime => _dayStartLocalTime;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await timeZone.InitializeAsync().ConfigureAwait(false);
            _dayStartLocalTime = await LoadDayStartAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
            _lastSeenDay = Today;
            preferences.Changed += OnPreferencesChanged;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public void CheckForDayChange()
    {
        if (!_initialized)
        {
            return;
        }

        var today = Today;
        if (today == _lastSeenDay)
        {
            return;
        }

        _lastSeenDay = today;
        RaiseDayChanged();
    }

    private async Task<TimeSpan> LoadDayStartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var prefs = await preferences.GetAsync(cancellationToken).ConfigureAwait(false);
            return prefs.DayStartLocalTime;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not load the day-start preference. Using midnight.");
            return TimeSpan.Zero;
        }
    }

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        _ = RefreshDayStartAsync();
    }

    private async Task RefreshDayStartAsync()
    {
        try
        {
            var dayStart = await LoadDayStartAsync(CancellationToken.None).ConfigureAwait(false);
            var previousDay = _lastSeenDay;
            _dayStartLocalTime = dayStart;

            var today = Today;
            if (today == previousDay)
            {
                return;
            }

            _lastSeenDay = today;
            RaiseDayChanged();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not refresh the effective local day after a preference change.");
        }
    }

    private void RaiseDayChanged()
    {
        var handlers = DayChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (var subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "A day-change subscriber failed.");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_initialized)
        {
            preferences.Changed -= OnPreferencesChanged;
        }

        _initGate.Dispose();
    }
}
