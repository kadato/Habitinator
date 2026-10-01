namespace App.Shared.RCL.Models;

/// <summary>
/// Period math for habit counter resets. Daily resets each local day,
/// weekly resets on Monday, monthly resets on the first of the month.
/// The board's local today already applies timezone and day-start,
/// so callers pass that day in.
/// </summary>
public static class HabitResetSchedule
{
    public static DateOnly PeriodStartFor(DateOnly today, HabitResetPeriod period) =>
        period switch
        {
            HabitResetPeriod.Weekly => StartOfWeekMonday(today),
            HabitResetPeriod.Monthly => new DateOnly(today.Year, today.Month, 1),
            _ => today,
        };

    public static DateOnly StartOfWeekMonday(DateOnly day)
    {
        var diff = ((int)day.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return day.AddDays(-diff);
    }

    /// <summary>
    /// True when stored counters belong to an older period and must show as zero.
    /// A null anchor is a legacy row from before anchors existed: initialize it
    /// without wiping so the upgrade keeps one period of counters.
    /// </summary>
    public static bool NeedsReset(DateOnly? anchor, DateOnly today, HabitResetPeriod period)
    {
        if (anchor is null)
        {
            return false;
        }

        return anchor.Value != PeriodStartFor(today, period);
    }

    public static (int Counter, int NegativeCounter) EffectiveCounters(
        int counter,
        int negativeCounter,
        DateOnly? anchor,
        DateOnly today,
        HabitResetPeriod period) =>
        NeedsReset(anchor, today, period) ? (0, 0) : (counter, negativeCounter);

    public static DateOnly EffectiveAnchor(DateOnly? anchor, DateOnly today, HabitResetPeriod period) =>
        anchor ?? PeriodStartFor(today, period);
}
