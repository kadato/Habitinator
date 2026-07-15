using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

public sealed class UserTimeZoneService : IUserTimeZoneService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly IClock _clock;
    private string? _timeZoneId;
    private string? _overrideTimeZoneId;
    private int _utcOffsetMinutes;
    private TimeZoneInfo? _timeZoneInfo;
    private bool _initialized;

    public UserTimeZoneService(IJSRuntime jsRuntime, IClock clock)
    {
        _jsRuntime = jsRuntime;
        _clock = clock;
    }

    public string? TimeZoneId => _timeZoneId;
    public int UtcOffsetMinutes => _utcOffsetMinutes;
    public bool IsDetected => _initialized && _timeZoneInfo is not null;

    public void SetOverride(string? timeZoneId)
    {
        _overrideTimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId;
        if (!_initialized)
        {
            return;
        }

        ApplyOverrideIfNeeded();
    }

    public DateOnly LocalToday => GetLocalDate(_clock.UtcNow);
    public DateOnly LocalYesterday => GetLocalDate(_clock.UtcNow.AddDays(-1));

    public DateTimeOffset ConvertToLocal(DateTimeOffset utcTime)
    {
        EnsureInitialized();

        if (_timeZoneInfo is null)
        {
            // Fallback: use the offset we detected from JS
            return utcTime.AddMinutes(-_utcOffsetMinutes);
        }

        try
        {
            return TimeZoneInfo.ConvertTime(utcTime, _timeZoneInfo);
        }
        catch
        {
            // Fallback if conversion fails
            return utcTime.AddMinutes(-_utcOffsetMinutes);
        }
    }

    public DateTimeOffset ConvertToUtc(DateTimeOffset localTime)
    {
        EnsureInitialized();

        if (_timeZoneInfo is null)
        {
            return localTime.AddMinutes(_utcOffsetMinutes);
        }

        try
        {
            // Convert from local to UTC
            var unspecified = DateTime.SpecifyKind(localTime.DateTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, _timeZoneInfo);
        }
        catch
        {
            return localTime.AddMinutes(_utcOffsetMinutes);
        }
    }

    public TimeSpan ConvertLocalTimeToUtc(TimeSpan localTime)
    {
        EnsureInitialized();

        // Create a datetime with the local time today in the user's timezone
        var localToday = LocalToday;
        var timeOnly = TimeOnly.FromTimeSpan(localTime);
        var localDateTime = localToday.ToDateTime(timeOnly);
        var localDto = new DateTimeOffset(localDateTime, TimeSpan.FromMinutes(-_utcOffsetMinutes));

        var utcDto = ConvertToUtc(localDto);
        return utcDto.TimeOfDay;
    }

    public TimeSpan ConvertUtcTimeToLocal(TimeSpan utcTime)
    {
        EnsureInitialized();

        // Create a datetime with the UTC time today
        var utcToday = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var timeOnly = TimeOnly.FromTimeSpan(utcTime);
        var utcDateTime = utcToday.ToDateTime(timeOnly);
        var utcDto = new DateTimeOffset(utcDateTime, TimeSpan.Zero);

        var localDto = ConvertToLocal(utcDto);
        return localDto.TimeOfDay;
    }

    private DateOnly GetLocalDate(DateTimeOffset utcNow)
    {
        var local = ConvertToLocal(utcNow);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        // Synchronous initialization is not possible with JS interop
        // The InitializeAsync method should be called first
        // This is a fallback that uses system local time
        try
        {
            _timeZoneInfo = TimeZoneInfo.Local;
            _utcOffsetMinutes = (int)_timeZoneInfo.BaseUtcOffset.TotalMinutes;
            _timeZoneId = _timeZoneInfo.Id;
        }
        catch
        {
            _utcOffsetMinutes = 0;
        }

        _initialized = true;
        ApplyOverrideIfNeeded();
    }

    /// <summary>
    ///     Initialize the timezone from the browser/device.
    ///     Should be called once at app startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            await _jsRuntime.InvokeVoidAsync("habitinatorLoadScript", "_content/App.Shared.RCL/js/userTimezone.js").ConfigureAwait(false);

            // Get timezone ID from browser
            var timeZoneId = await _jsRuntime.InvokeAsync<string?>("habitinatorGetUserTimezone").ConfigureAwait(false);

            // Get offset in minutes (negative for East of UTC, positive for West)
            var offsetMinutes = await _jsRuntime.InvokeAsync<int>("habitinatorGetTimezoneOffsetMinutes").ConfigureAwait(false);
            _utcOffsetMinutes = offsetMinutes;

            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                _timeZoneId = timeZoneId;

                // Try to find the timezone on the server
                try
                {
                    _timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                }
                catch
                {
                    // Timezone not found on this system - create a custom one from offset
                    // This handles cases like "Europe/Budapest" on Windows which uses different IDs
                    var offset = TimeSpan.FromMinutes(-offsetMinutes); // offsetMinutes is negative for East
                    _timeZoneInfo = TimeZoneInfo.CreateCustomTimeZone(
                        timeZoneId,
                        offset,
                        timeZoneId,
                        timeZoneId);
                }
            }
            else
            {
                // Fallback to system local timezone
                _timeZoneInfo = TimeZoneInfo.Local;
                _timeZoneId = _timeZoneInfo.Id;
            }
        }
        catch (JSDisconnectedException)
        {
            // JS runtime not available (e.g., prerendering)
            _timeZoneInfo = TimeZoneInfo.Local;
            _timeZoneId = _timeZoneInfo?.Id;
        }
        catch
        {
            // Any other error, use system local
            _timeZoneInfo = TimeZoneInfo.Local;
            _timeZoneId = _timeZoneInfo?.Id;
        }

        _initialized = true;
        ApplyOverrideIfNeeded();
    }

    private void ApplyOverrideIfNeeded()
    {
        if (string.IsNullOrWhiteSpace(_overrideTimeZoneId))
        {
            return;
        }

        try
        {
            _timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(_overrideTimeZoneId);
            _timeZoneId = _overrideTimeZoneId;
            _utcOffsetMinutes = (int)_timeZoneInfo.BaseUtcOffset.TotalMinutes;
        }
        catch
        {
            // Ignore invalid override and keep detected timezone.
        }
    }

    /// <summary>
    ///     Gets the timezone abbreviation for display purposes (e.g., "EST", "UTC+2").
    /// </summary>
    public string GetTimeZoneAbbreviation()
    {
        if (!IsDetected)
        {
            return "UTC";
        }

        var offset = TimeSpan.FromMinutes(-_utcOffsetMinutes);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var hours = Math.Abs(offset.Hours);
        var minutes = Math.Abs(offset.Minutes);

        if (minutes == 0)
        {
            return $"UTC{sign}{hours}";
        }

        return $"UTC{sign}{hours}:{minutes:D2}";
    }
}
