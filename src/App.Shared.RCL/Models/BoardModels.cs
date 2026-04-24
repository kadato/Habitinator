using System.Text.Json;

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

/// <summary>How often a daily (or its checklist due logic) is scheduled to repeat.</summary>
public enum DailyRepeatType
{
    Daily,
    Weekly,
    Monthly,
    Yearly
}

public sealed record DailyChecklistItem(Guid Id, string Text, bool IsDone = false);

/// <param name="Counter">For habits: + counter when <see cref="TrackPlus"/> is enabled. For dailies: current streak (manual in edit dialog).</param>
/// <param name="NegativeCounter">Tally for the − button when <see cref="TrackMinus"/> is enabled.</param>
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
    /// <summary>Start date for scheduling (dailies only; null = unset).</summary>
    DateOnly? DailyStartDate = null,
    DailyRepeatType DailyRepeat = DailyRepeatType.Daily,
    int DailyRepeatInterval = 1,
    string? ChecklistJson = null,
    /// <summary>UTC calendar day the daily was last checked off. Used for dailies; null = not completed for this cycle.</summary>
    DateOnly? DailyLastCompletedOn = null,
    /// <summary>Due date for to-dos; stored in the same DB column as daily start when section is to-do.</summary>
    DateOnly? TodoDueDate = null);

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

        foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IReadOnlyList<DailyChecklistItem> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<DailyChecklistItem>();
        }

        try
        {
            DailyChecklistItem[]? rows = JsonSerializer.Deserialize<DailyChecklistItem[]>(json, s_options);
            return rows?.Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList() ?? [];
        }
        catch
        {
            return Array.Empty<DailyChecklistItem>();
        }
    }

    public static string? Serialize(IReadOnlyList<DailyChecklistItem> items)
    {
        var cleaned = items
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => new DailyChecklistItem(
                x.Id == Guid.Empty ? Guid.NewGuid() : x.Id,
                x.Text.Trim(),
                x.IsDone))
            .ToList();
        if (cleaned.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(cleaned, s_options);
    }
}
