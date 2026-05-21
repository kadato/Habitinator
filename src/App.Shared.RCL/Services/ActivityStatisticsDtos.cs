using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public sealed record ActivityWeekBarDto(int Index, DateOnly WeekStart, int EventCount, int FocusMinutes);

public sealed record ActivityHeatmapCellDto(
    int DayRow,
    int WeekCol,
    DateOnly Date,
    int Count,
    int Intensity,
    bool InDataRange);

public static class DailyGraphPeriods
{
    public const string Rolling370Days = "r370";
    public const string YearPrefix = "y";

    public static string ForCalendarYear(int year)
    {
        return $"{YearPrefix}{year}";
    }
}

/// <summary>GitHub-style contribution grid for one daily in the selected period.</summary>
public sealed record DailyContributionGraphDto(
    Guid BoardItemId,
    string Title,
    IReadOnlyList<ActivityHeatmapCellDto> Heatmap,
    int GridWeekColumns,
    int MaxDayCountInRange);

public sealed record DailyGraphPeriodOption(string Key, string Label);

public sealed record DailyContributionsViewDto(
    string PeriodKey,
    IReadOnlyList<DailyGraphPeriodOption> PeriodOptions,
    IReadOnlyList<DailyContributionGraphDto> Graphs,
    DateOnly RangeStart,
    DateOnly RangeEnd);

public sealed record ActivityDayEventDto(
    DateTimeOffset OccurredAtUtc,
    ActivityEventType EventType,
    string Label,
    string? BoardItemTitle,
    int? DurationSeconds,
    string? CustomLabel = null);

public sealed record ActivityDayDetailDto(
    DateOnly Date,
    IReadOnlyList<ActivityDayEventDto> Events,
    int FocusMinutesTotal);

public sealed record ActivityDashboardDto(
    string PeriodKey,
    IReadOnlyList<ActivityWeekBarDto> WeekBars,
    IReadOnlyList<ActivityHeatmapCellDto> Heatmap,
    int GridWeekColumns,
    int TotalEvents,
    int TotalFocusMinutes,
    int MaxDayCount,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    int HeatmapDataDayCount,
    DateOnly WeekBarsRangeStart,
    DateOnly WeekBarsRangeEnd,
    IReadOnlyList<string> AvailableTags);
