namespace App.Shared.RCL.Models;

/// <summary>Habit goal helpers shared by the board, editors, stats, and sync.</summary>
public static class HabitGoal
{
    public const int MaxTargetCount = 1_000_000;

    /// <summary>Null or zero means no goal. Negative values and values above the cap are clamped.</summary>
    public static int? Normalize(int? targetCount) =>
        targetCount is > 0 ? Math.Min(targetCount.Value, MaxTargetCount) : null;

    public static bool HasGoal(BoardItem item) => item.TargetCount is > 0;

    public static bool IsMet(BoardItem item) =>
        item.TargetCount is > 0 && item.Counter >= item.TargetCount.Value;

    public static int ProgressPercent(BoardItem item)
    {
        if (item.TargetCount is not > 0)
        {
            return 0;
        }

        return (int)Math.Clamp(100L * item.Counter / item.TargetCount.Value, 0, 100);
    }

    /// <summary>Short period word used in labels such as "4 of 8 today".</summary>
    public static string PeriodWord(HabitResetPeriod period) => period switch
    {
        HabitResetPeriod.Weekly => "this week",
        HabitResetPeriod.Monthly => "this month",
        _ => "today"
    };

    /// <summary>Compact unit used in stats labels, for example "8/day".</summary>
    public static string PeriodUnit(HabitResetPeriod period) => period switch
    {
        HabitResetPeriod.Weekly => "week",
        HabitResetPeriod.Monthly => "month",
        _ => "day"
    };
}
