namespace App.Shared.RCL.Models;

/// <summary>
/// Bit mask for weekly daily schedules. Bit <c>1 &lt;&lt; (int)DayOfWeek</c>, so Sunday is 1,
/// Monday is 2, Tuesday is 4, Wednesday is 8, Thursday is 16, Friday is 32, Saturday is 64.
/// Zero means no mask, the legacy weekly behavior of repeating on the start date's weekday.
/// </summary>
public static class DailyWeekdays
{
    public const int None = 0;
    public const int All = 127;

    public static int Normalize(int? mask)
    {
        if (mask is null)
        {
            return None;
        }

        return mask.Value & All;
    }

    public static int Normalize(int mask) => mask & All;

    public static bool Has(int mask, DayOfWeek day) => (Normalize(mask) & (1 << (int)day)) != 0;

    public static bool HasAny(int mask) => Normalize(mask) != None;

    public static int From(params DayOfWeek[] days)
    {
        var mask = None;
        foreach (var d in days)
        {
            mask |= 1 << (int)d;
        }

        return mask;
    }

    public static IReadOnlyList<DayOfWeek> ToDays(int mask)
    {
        List<DayOfWeek> days = [];
        var n = Normalize(mask);
        for (var i = 0; i < 7; i++)
        {
            if ((n & (1 << i)) != 0)
            {
                days.Add((DayOfWeek)i);
            }
        }

        return days;
    }
}
