using App.Shared.RCL.Services;

namespace App.Web.Services;

/// <summary>
///     Server-side timezone resolved from the signed-in user's stored timezone id via
///     <see cref="SetOverride" />, falling back to UTC. The web app is WASM, so the JS-based
///     <see cref="UserTimeZoneService" /> is unusable on the server. Without this, all server-side
///     date math for streaks, retro validation, and statistics would run in the host's timezone.
/// </summary>
public sealed class ServerTimeZoneService : IUserTimeZoneService
{
    private TimeZoneInfo? _timeZoneInfo;

    public string? TimeZoneId => _timeZoneInfo?.Id;

    public bool IsDetected => _timeZoneInfo is not null;

    public void SetOverride(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            _timeZoneInfo = null;
            return;
        }

        try
        {
            _timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            _timeZoneInfo = null;
        }
        catch (InvalidTimeZoneException)
        {
            _timeZoneInfo = null;
        }
    }

    public DateOnly LocalToday => DateOnly.FromDateTime(ConvertToLocal(DateTimeOffset.UtcNow).DateTime);

    public Task InitializeAsync() => Task.CompletedTask;

    public DateTimeOffset ConvertToLocal(DateTimeOffset utcTime) =>
        _timeZoneInfo is null ? utcTime : TimeZoneInfo.ConvertTime(utcTime, _timeZoneInfo);

    public DateTimeOffset ConvertToUtc(DateTimeOffset localTime) =>
        _timeZoneInfo is null
            ? localTime
            : TimeZoneInfo.ConvertTimeToUtc(localTime.DateTime, _timeZoneInfo);

    public TimeSpan ConvertLocalTimeToUtc(TimeSpan localTime)
    {
        var localToday = LocalToday;
        var localDto = new DateTimeOffset(
            localToday.ToDateTime(TimeOnly.FromTimeSpan(localTime)),
            _timeZoneInfo?.GetUtcOffset(DateTimeOffset.UtcNow) ?? TimeSpan.Zero);
        return ConvertToUtc(localDto).TimeOfDay;
    }

    public TimeSpan ConvertUtcTimeToLocal(TimeSpan utcTime)
    {
        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        var utcDto = new DateTimeOffset(utcToday.ToDateTime(TimeOnly.FromTimeSpan(utcTime)), TimeSpan.Zero);
        return ConvertToLocal(utcDto).TimeOfDay;
    }

    public string GetTimeZoneAbbreviation() => "UTC";
}
