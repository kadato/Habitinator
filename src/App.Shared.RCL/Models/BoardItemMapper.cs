namespace App.Shared.RCL.Models;

/// <summary>
/// The single place that applies the user's local day to a board item.
/// Every host maps stored rows to models through here so daily completion
/// and habit counter resets agree: server snapshots, sync deltas, data
/// export, and the offline mirror. Callers copy stored fields verbatim
/// first, including any computed daily streak as <see cref="BoardItem.Counter" />,
/// then run the result through <see cref="WithLocalDay" />.
/// </summary>
public static class BoardItemMapper
{
    public static BoardItem WithLocalDay(BoardItem item, BoardSection section, DateOnly today)
    {
        if (section == BoardSection.Daily)
        {
            return item with
            {
                IsCompleted = DailySchedule.IsCompletedForToday(item.DailyLastCompletedOn, item.IsCompleted, today),
            };
        }

        if (section == BoardSection.Habit)
        {
            var (counter, negative) = HabitResetSchedule.EffectiveCounters(
                item.Counter, item.NegativeCounter, item.HabitPeriodStart, today, item.ResetPeriod);
            return item with
            {
                Counter = counter,
                NegativeCounter = negative,
                HabitPeriodStart = HabitResetSchedule.EffectiveAnchor(item.HabitPeriodStart, today, item.ResetPeriod),
            };
        }

        return item;
    }
}
