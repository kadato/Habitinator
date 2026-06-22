using App.Shared.RCL.Models;

using FsCheck;
using FsCheck.Xunit;

namespace App.Shared.Tests;

public sealed class DailyScheduleFuzzTests
{
    private static DateOnly ToDateOnly(DateTime dt) => DateOnly.FromDateTime(dt);

    [Property]
    public void IsScheduledOn_NeverThrows(DateTime? startOpt, int repeatVal, int interval, DateTime onDt)
    {
        DateOnly? start = startOpt.HasValue ? ToDateOnly(startOpt.Value) : null;
        DateOnly on = ToDateOnly(onDt);
        var repeat = (DailyRepeatType)(Math.Abs(repeatVal) % 4);

        // This call should be completely exception-safe for any input
        var exception = Xunit.Record.Exception(() => DailySchedule.IsScheduledOn(start, repeat, interval, on));
        Assert.Null(exception);
    }

    [Property]
    public void IsScheduledOn_BeforeStart_AlwaysFalse(DateTime startDt, int repeatVal, int interval, DateTime onDt)
    {
        DateOnly start = ToDateOnly(startDt);
        DateOnly on = ToDateOnly(onDt);
        var repeat = (DailyRepeatType)(Math.Abs(repeatVal) % 4);

        if (on < start)
        {
            var result = DailySchedule.IsScheduledOn(start, repeat, interval, on);
            Assert.False(result);
        }
    }

    [Property]
    public void IsScheduledOn_IntervalNormalization(DateTime? startOpt, int repeatVal, int interval, DateTime onDt)
    {
        DateOnly? start = startOpt.HasValue ? ToDateOnly(startOpt.Value) : null;
        DateOnly on = ToDateOnly(onDt);
        var repeat = (DailyRepeatType)(Math.Abs(repeatVal) % 4);

        var actualResult = DailySchedule.IsScheduledOn(start, repeat, interval, on);

        int normalizedInterval = Math.Max(1, Math.Min(999, interval < 1 ? 1 : interval));
        var expectedResult = DailySchedule.IsScheduledOn(start, repeat, normalizedInterval, on);

        Assert.Equal(expectedResult, actualResult);
    }

    [Property]
    public void StreakHistoryScheduleStart_NeverThrows(DateTime? startOpt, DateTime notAfterDt, int repeatVal, int interval, int streakWindow)
    {
        DateOnly? start = startOpt.HasValue ? ToDateOnly(startOpt.Value) : null;
        DateOnly notAfter = ToDateOnly(notAfterDt);
        var repeat = (DailyRepeatType)(Math.Abs(repeatVal) % 4);

        var exception = Xunit.Record.Exception(() => DailySchedule.StreakHistoryScheduleStart(start, notAfter, repeat, interval, streakWindow));
        Assert.Null(exception);
    }

    [Property]
    public void StreakHistoryScheduleStart_Invariants(DateTime? startOpt, DateTime notAfterDt, int repeatVal, int interval, int streakWindow)
    {
        DateOnly? start = startOpt.HasValue ? ToDateOnly(startOpt.Value) : null;
        DateOnly notAfter = ToDateOnly(notAfterDt);
        var repeat = (DailyRepeatType)(Math.Abs(repeatVal) % 4);

        var result = DailySchedule.StreakHistoryScheduleStart(start, notAfter, repeat, interval, streakWindow);

        if (start.HasValue && start.Value < notAfter)
        {
            Assert.Equal(start.Value, result);
        }
        else
        {
            Assert.True(result <= notAfter);
        }
    }
}
