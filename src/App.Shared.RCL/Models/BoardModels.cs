using System.Text.Json;

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

public sealed record DailyChecklistItem(Guid Id, string Text, bool IsDone = false);

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
    /// <summary>When set, completing the to-do advances its due date by this many days, a recurring to-do.</summary>
    int? TodoRepeatIntervalDays = null,
    /// <summary>Server row version for optimistic concurrency and incremental sync. Maps to <c>UpdatedAtUtc</c> on the server.</summary>
    DateTimeOffset? ServerUpdatedAtUtc = null,
    /// <summary>Server creation time for display and audit only. Do not use for ordering.</summary>
    DateTimeOffset? CreatedAtUtc = null,
    /// <summary>Explicit user-defined sort position. New items get max+1. Reorder sets midpoint between neighbours.</summary>
    double? SortOrder = null,
    /// <summary>True if the item is archived and hidden from the active board.</summary>
    bool IsArchived = false);

public sealed record BoardSnapshot(
    IReadOnlyList<BoardItem> Habits,
    IReadOnlyList<BoardItem> Dailies,
    IReadOnlyList<BoardItem> Todos);

public static class BoardTagUtil
{
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
                x.IsDone))
            .ToList();
        if (cleaned.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(cleaned, JsonDefaults.Storage);
    }

    public static string? Normalize(string? json) => Serialize(Parse(json));
}
