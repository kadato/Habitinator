using System.Text.Json;
using System.Text.Json.Serialization;

using App.Shared.RCL.Services;

namespace App.Shared.RCL.Models;

public enum BoardSection
{
    Habit,
    Daily,
    Todo
}

public enum HabitResetPeriod
{
    Daily,
    Weekly,
    Monthly
}

/// <summary>How often a daily, or its checklist due logic, is scheduled to repeat.</summary>
public enum DailyRepeatType
{
    Daily,
    Weekly,
    Monthly,
    Yearly
}

/// <param name="DoneOn">
///     Local day the subtask was checked. The check stays current for the rest of the daily's
///     repeat cycle. Rows written before this field existed have no day, so the next cycle
///     rollover clears their checks.
/// </param>
public sealed record DailyChecklistItem(
    Guid Id,
    string Text,
    bool IsDone = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? DoneOn = null);

/// <param name="Counter">
///     For habits: + counter when <see cref="TrackPlus" /> is enabled. For dailies: the larger of the
///     event-derived streak and the <c>Counter</c> column, representing the last value from the daily edit dialog and
///     backfill, so a manual change is visible in the API/UI even when the computed streak lags.
/// </param>
/// <param name="NegativeCounter">Tally for the − button when <see cref="TrackMinus" /> is enabled.</param>
public sealed record BoardItem(
    Guid Id,
    string Title,
    bool IsCompleted = false,
    int Counter = 0,
    string? Notes = null,
    string? Tags = null,
    bool TrackPlus = true,
    bool TrackMinus = true,
    int NegativeCounter = 0,
    HabitResetPeriod ResetPeriod = HabitResetPeriod.Daily,
    /// <summary>Start date for scheduling. Dailies only, null means unset.</summary>
    DateOnly? DailyStartDate = null,
    DailyRepeatType DailyRepeat = DailyRepeatType.Daily,
    int DailyRepeatInterval = 1,
    string? ChecklistJson = null,
    /// <summary>UTC calendar day the daily was last checked off. Used for dailies. Null = not completed for this cycle.</summary>
    DateOnly? DailyLastCompletedOn = null,
    /// <summary>Due date for to-dos. Stored in the same DB column as daily start when section is to-do.</summary>
    DateOnly? TodoDueDate = null,
    /// <summary>Server row version for optimistic concurrency and incremental sync. Maps to <c>UpdatedAtUtc</c> on the server.</summary>
    DateTimeOffset? ServerUpdatedAtUtc = null,
    /// <summary>Server creation time for display and audit only. Do not use for ordering.</summary>
    DateTimeOffset? CreatedAtUtc = null,
    /// <summary>Explicit user-defined sort position. New items get max+1. Reorder sets midpoint between neighbours.</summary>
    double? SortOrder = null,
    /// <summary>True if the item is archived and hidden from the active board.</summary>
    bool IsArchived = false,
    /// <summary>Local-day start of the period the habit counters belong to. Null for rows written before resets existed.</summary>
    DateOnly? HabitPeriodStart = null,
    /// <summary>
    /// Weekly weekday mask, see <see cref="DailyWeekdays" />. Zero means no mask, the legacy
    /// behavior of repeating on the start date's weekday. Only honored when
    /// <see cref="DailyRepeat" /> is <see cref="DailyRepeatType.Weekly" />.
    /// </summary>
    int DailyWeekdays = 0,
    /// <summary>
    /// The day a postponed daily occurrence now falls on. The occurrence is not due on the
    /// days it was moved from, and it is due on this day even when the repeat schedule would
    /// not place it here. The repeat schedule is unchanged.
    /// </summary>
    DateOnly? DailyPostponedUntil = null,
    /// <summary>
    /// The first day the postponed occurrence was moved from. Null means the only moved day
    /// is the day before <see cref="DailyPostponedUntil" />.
    /// </summary>
    DateOnly? DailyPostponedFrom = null,
    /// <summary>
    /// Optional habit goal for the current reset period. Null or zero means the habit has no goal.
    /// The plus counter is measured against this target.
    /// </summary>
    int? TargetCount = null,
    /// <summary>
    /// The local day this daily occurrence was skipped. A matching day is not due, but the
    /// day still bridges the streak without adding to it.
    /// </summary>
    DateOnly? DailySkippedOn = null);

public sealed record BoardSnapshot(
    IReadOnlyList<BoardItem> Habits,
    IReadOnlyList<BoardItem> Dailies,
    IReadOnlyList<BoardItem> Todos,
    int ProtocolVersion = BoardProtocolVersion.Current);

public static class BoardTagUtil
{
    public const int MaxTagsLength = 500;

    public static IEnumerable<string> ParseTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length > 0)
            {
                yield return part;
            }
        }
    }

    public static string? Canonicalize(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        return string.Join(",", ParseTags(raw));
    }

    public static void EnsureLength(string? canonicalTags, string field = "tags")
    {
        if (canonicalTags is not null && canonicalTags.Length > MaxTagsLength)
        {
            throw new ArgumentException($"{field}: expected at most {MaxTagsLength} chars, got {canonicalTags.Length} chars.", field);
        }
    }
}

public static class DailyChecklistJson
{
    public static IReadOnlyList<DailyChecklistItem> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var rows = JsonSerializer.Deserialize<DailyChecklistItem[]>(json, JsonDefaults.Storage);
            return rows?.Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string? Serialize(IReadOnlyList<DailyChecklistItem> items)
    {
        var cleaned = items
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => new DailyChecklistItem(
                x.Id == Guid.Empty ? Guid.NewGuid() : x.Id,
                ZalgoSanitizer.SanitizeAndTrim(x.Text),
                x.IsDone,
                x.IsDone ? x.DoneOn : null))
            .ToList();
        if (cleaned.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(cleaned, JsonDefaults.Storage);
    }

    public static string? Normalize(string? json) => Serialize(Parse(json));

    /// <summary>
    ///     Clears subtask checks that belong to an earlier repeat cycle. A weekly daily keeps its
    ///     subtask checks through the off days, then resets them on the next scheduled day.
    ///     Unchecked rows and checks made in the current cycle stay as they are. Returns the
    ///     input unchanged when nothing is stale.
    /// </summary>
    public static string? ResetDoneForCycle(string? json, BoardItem daily, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(daily);
        var rows = Parse(json);
        if (rows.Count == 0)
        {
            return Normalize(json);
        }

        // Any check made today belongs to the current cycle, whatever the schedule.
        if (!rows.Any(x => x.IsDone && x.DoneOn != today))
        {
            return json;
        }

        var cycleStart = DailySchedule.CurrentCycleStart(daily, today);
        var stale = false;
        var adjusted = new List<DailyChecklistItem>(rows.Count);
        foreach (var row in rows)
        {
            if (row.IsDone && !IsInCurrentCycle(row.DoneOn, cycleStart, today))
            {
                adjusted.Add(row with { IsDone = false, DoneOn = null });
                stale = true;
            }
            else
            {
                adjusted.Add(row);
            }
        }

        return stale ? Serialize(adjusted) : json;
    }

    private static bool IsInCurrentCycle(DateOnly? doneOn, DateOnly? cycleStart, DateOnly today)
    {
        if (doneOn is not { } day || day > today)
        {
            return false;
        }

        return cycleStart is { } start ? day >= start : day == today;
    }

    /// <summary>
    ///     Sets <c>DoneOn</c> to <paramref name="today" /> on checked rows that have no day. Call
    ///     this for a daily when a write supplies <c>isDone</c> without <c>doneOn</c>. A check
    ///     made now belongs to today's cycle.
    /// </summary>
    public static string? StampUndatedDone(string? json, DateOnly today)
    {
        var rows = Parse(json);
        if (rows.Count == 0)
        {
            return Normalize(json);
        }

        var adjusted = rows
            .Select(x => x.IsDone && x.DoneOn is null ? x with { DoneOn = today } : x)
            .ToList();
        return Serialize(adjusted);
    }
}
