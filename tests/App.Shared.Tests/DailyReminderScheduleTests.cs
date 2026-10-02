using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class DailyReminderScheduleTests
{
    [Fact]
    public void NormalizeTime_FallsBackTo7am_OnInvalid()
    {
        DailyReminderSchedule.NormalizeTime(null).Should().Be(TimeSpan.FromHours(7));
        DailyReminderSchedule.NormalizeTime(TimeSpan.FromHours(-1)).Should().Be(TimeSpan.FromHours(7));
        DailyReminderSchedule.NormalizeTime(TimeSpan.FromDays(1)).Should().Be(TimeSpan.FromHours(7));
        DailyReminderSchedule.NormalizeTime(TimeSpan.FromHours(6.5)).Should().Be(TimeSpan.FromHours(6.5));
    }

    [Fact]
    public void NextLocalTime_TodayWhenStillAhead_ElseTomorrow()
    {
        var tz = new TestTimeZoneService();
        var utcNow = new DateTimeOffset(2026, 4, 26, 6, 0, 0, TimeSpan.Zero);

        var today = DailyReminderSchedule.NextLocalTime(TimeSpan.FromHours(7), utcNow, tz);
        today.Should().Be(new DateTime(2026, 4, 26, 7, 0, 0, DateTimeKind.Unspecified));

        var tomorrow = DailyReminderSchedule.NextLocalTime(TimeSpan.FromHours(5), utcNow, tz);
        tomorrow.Should().Be(new DateTime(2026, 4, 27, 5, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void NextLocalTime_HonorsTimezoneOverride()
    {
        // UTC 05:00 with +02:00 local means 07:00 local; a 08:00 reminder is still today.
        var tz = new OffsetTimeZone(TimeSpan.FromHours(2));
        var utcNow = new DateTimeOffset(2026, 4, 26, 5, 0, 0, TimeSpan.Zero);

        var next = DailyReminderSchedule.NextLocalTime(TimeSpan.FromHours(8), utcNow, tz);
        next.Should().Be(new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void NextLocalTime_DefersOutOfQuietHours()
    {
        var tz = new TestTimeZoneService();
        var utcNow = new DateTimeOffset(2026, 4, 26, 6, 0, 0, TimeSpan.Zero);
        var settings = NotificationSettings.CreateDefault();
        settings.QuietHoursEnabled = true;
        settings.QuietHoursStartUtc = TimeSpan.FromHours(6);
        settings.QuietHoursEndUtc = TimeSpan.FromHours(8);

        // 07:00 falls inside 06:00-08:00 quiet window, so it moves to 08:00.
        var next = DailyReminderSchedule.NextLocalTime(TimeSpan.FromHours(7), utcNow, tz, settings);
        next.Should().Be(new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void NextLocalTime_DefersAcrossMidnightWindow()
    {
        var tz = new TestTimeZoneService();
        var utcNow = new DateTimeOffset(2026, 4, 26, 21, 0, 0, TimeSpan.Zero);
        var settings = NotificationSettings.CreateDefault();
        settings.QuietHoursEnabled = true;
        settings.QuietHoursStartUtc = TimeSpan.FromHours(22);
        settings.QuietHoursEndUtc = TimeSpan.FromHours(6);

        // 23:00 falls inside 22:00-06:00, so it moves to 06:00 next day.
        var next = DailyReminderSchedule.NextLocalTime(TimeSpan.FromHours(23), utcNow, tz, settings);
        next.Should().Be(new DateTime(2026, 4, 27, 6, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void ResolveToday_AppliesDayStartRollback()
    {
        var tz = new TestTimeZoneService();
        // 02:00 UTC with a 05:00 day start still belongs to yesterday's board calendar.
        var today = DailyReminderSchedule.ResolveToday(
            new DateTimeOffset(2026, 4, 26, 2, 0, 0, TimeSpan.Zero),
            tz,
            TimeSpan.FromHours(5));
        today.Should().Be(new DateOnly(2026, 4, 25));
    }

    [Fact]
    public void ShouldShowWebReminder_FiresAfterTime_OncePerDay()
    {
        var tz = new TestTimeZoneService();
        var clock = new FixedClock(new DateTimeOffset(2026, 4, 26, 8, 0, 0, TimeSpan.Zero));
        var settings = NotificationSettings.CreateDefault();
        settings.DailyReminderTime = TimeSpan.FromHours(7);

        DailyReminderSchedule.ShouldShowWebReminder(settings, TimeSpan.Zero, tz, clock, lastDismissedOn: null)
            .Should().BeTrue();
        DailyReminderSchedule.ShouldShowWebReminder(settings, TimeSpan.Zero, tz, clock, new DateOnly(2026, 4, 26))
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldShowWebReminder_HidesBeforeTime_AndInsideQuietHours()
    {
        var tz = new TestTimeZoneService();

        var early = new FixedClock(new DateTimeOffset(2026, 4, 26, 6, 0, 0, TimeSpan.Zero));
        var settings = NotificationSettings.CreateDefault();
        settings.DailyReminderTime = TimeSpan.FromHours(7);
        DailyReminderSchedule.ShouldShowWebReminder(settings, TimeSpan.Zero, tz, early, null).Should().BeFalse();

        var quietClock = new FixedClock(new DateTimeOffset(2026, 4, 26, 7, 30, 0, TimeSpan.Zero));
        var quiet = NotificationSettings.CreateDefault();
        quiet.DailyReminderTime = TimeSpan.FromHours(7);
        quiet.QuietHoursEnabled = true;
        quiet.QuietHoursStartUtc = TimeSpan.FromHours(6);
        quiet.QuietHoursEndUtc = TimeSpan.FromHours(9);
        DailyReminderSchedule.ShouldShowWebReminder(quiet, TimeSpan.Zero, tz, quietClock, null).Should().BeFalse();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class OffsetTimeZone(TimeSpan offset) : IUserTimeZoneService
    {
        public string? TimeZoneId => "Test";
        public bool IsDetected => true;
        public void SetOverride(string? timeZoneId) { }
        public DateOnly LocalToday => DateOnly.FromDateTime(DateTime.UtcNow + offset);
        public Task InitializeAsync() => Task.CompletedTask;
        public DateTimeOffset ConvertToLocal(DateTimeOffset utcTime) => utcTime.ToOffset(offset);
        public DateTimeOffset ConvertToUtc(DateTimeOffset localTime) => localTime.ToUniversalTime();
        public TimeSpan ConvertLocalTimeToUtc(TimeSpan localTime) => localTime - offset;
        public TimeSpan ConvertUtcTimeToLocal(TimeSpan utcTime) => utcTime + offset;
        public string GetTimeZoneAbbreviation() => "T";
    }
}
