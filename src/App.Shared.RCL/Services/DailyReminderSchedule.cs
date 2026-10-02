using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>
/// Pure scheduling math for the daily reminder, shared by MAUI local notifications
/// and the web in-app reminder. It reads `IUserTimeZoneService` and `DayStartLocalTime`,
/// so the reminder fires on the same calendar the board uses. It moves the reminder out of quiet hours.
/// </summary>
public static class DailyReminderSchedule
{
    public static readonly TimeSpan FallbackTime = TimeSpan.FromHours(7);

    public static TimeSpan NormalizeTime(TimeSpan? timeOfDay)
    {
        if (timeOfDay is not { } t || t < TimeSpan.Zero || t >= TimeSpan.FromDays(1))
        {
            return FallbackTime;
        }

        return t;
    }

    public static DateOnly ResolveToday(DateTimeOffset utcNow, IUserTimeZoneService? tz, TimeSpan? dayStartLocalTime)
    {
        return DailySchedule.LocalDay(utcNow, tz, dayStartLocalTime);
    }

    /// <summary>
    /// Next reminder in the user's local time. Today if still ahead, else tomorrow.
    /// When the candidate falls inside quiet hours, the schedule moves it to the end of the window.
    /// </summary>
    public static DateTime NextLocalTime(
        TimeSpan timeOfDay,
        DateTimeOffset utcNow,
        IUserTimeZoneService? tz,
        NotificationSettings? settings = null)
    {
        timeOfDay = NormalizeTime(timeOfDay);

        var localNow = tz is { IsDetected: true } ? tz.ConvertToLocal(utcNow) : utcNow;
        var localDateTime = localNow.DateTime;
        var candidate = localDateTime.Date + timeOfDay;
        if (candidate <= localDateTime)
        {
            candidate = candidate.AddDays(1);
        }

        if (settings is not null)
        {
            candidate = DeferOutOfQuietHours(candidate, settings, tz);
        }

        return candidate;
    }

    internal static DateTime DeferOutOfQuietHours(DateTime candidateLocal, NotificationSettings settings, IUserTimeZoneService? tz)
    {
        if (!settings.QuietHoursEnabled
            || !settings.QuietHoursStartUtc.HasValue
            || !settings.QuietHoursEndUtc.HasValue)
        {
            return candidateLocal;
        }

        TimeSpan startLocal;
        TimeSpan endLocal;
        if (tz is { IsDetected: true })
        {
            startLocal = tz.ConvertUtcTimeToLocal(settings.QuietHoursStartUtc.Value);
            endLocal = tz.ConvertUtcTimeToLocal(settings.QuietHoursEndUtc.Value);
        }
        else
        {
            startLocal = settings.QuietHoursStartUtc.Value;
            endLocal = settings.QuietHoursEndUtc.Value;
        }

        if (startLocal == endLocal)
        {
            return candidateLocal;
        }

        var time = candidateLocal.TimeOfDay;
        bool inQuiet = startLocal < endLocal
            ? time >= startLocal && time < endLocal
            : time >= startLocal || time < endLocal;

        if (!inQuiet)
        {
            return candidateLocal;
        }

        var deferred = candidateLocal.Date + endLocal;
        if (deferred <= candidateLocal)
        {
            deferred = deferred.AddDays(1);
        }

        return deferred;
    }

    /// <summary>
    /// Whether the web in-app reminder shows now: enabled, reminder time has
    /// passed on the board calendar, out of quiet hours, and not already dismissed today.
    /// </summary>
    public static bool ShouldShowWebReminder(
        NotificationSettings settings,
        TimeSpan? dayStartLocalTime,
        IUserTimeZoneService? tz,
        IClock clock,
        DateOnly? lastDismissedOn,
        DateTime? nowLocalOverride = null)
    {
        if (!settings.DailyReminderEnabled || !settings.DailyReminderTime.HasValue)
        {
            return false;
        }

        var utcNow = clock.UtcNow;
        var today = DailySchedule.LocalDay(utcNow, tz, dayStartLocalTime);
        if (lastDismissedOn == today)
        {
            return false;
        }

        var localNow = nowLocalOverride
            ?? (tz is { IsDetected: true } ? tz.ConvertToLocal(utcNow).DateTime : utcNow.DateTime);

        var reminderTime = NormalizeTime(settings.DailyReminderTime);
        if (localNow.TimeOfDay < reminderTime)
        {
            return false;
        }

        // Suppress inside quiet hours; the MAUI path defers, the web path waits.
        if (settings.QuietHoursEnabled
            && settings.QuietHoursStartUtc.HasValue
            && settings.QuietHoursEndUtc.HasValue)
        {
            var deferred = DeferOutOfQuietHours(
                localNow.Date + localNow.TimeOfDay,
                settings,
                tz);
            if (deferred > localNow)
            {
                return false;
            }
        }

        return true;
    }
}
