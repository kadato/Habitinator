#pragma warning disable S3881 // Dispose is implemented in the generated Razor part
#pragma warning disable S1144, S4487, IDE0051, IDE0052
using System.Globalization;

using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class StatisticsPanel : IDisposable
{
    private readonly Dictionary<Guid, Dictionary<(int Row, int Col), ActivityHeatmapCellDto>> _dailyCellIndices = [];
    private readonly Dictionary<Guid, Dictionary<(int Row, int Col), ActivityHeatmapCellDto>> _habitCellIndices = [];

    private PersistingComponentStateSubscription _subscription;
    private ActivityDashboardDto? _data;
    private IReadOnlyList<DailyGraphPeriodOption>? _periodOptions;
    private Dictionary<(int R, int C), ActivityHeatmapCellDto> _cellIndex = [];

    private int _weekBarMax;
    private bool _loading = true;
    private bool _periodBusy;
    private string? _error;

    private DailyContributionsViewDto? _dailyView;
    private HabitContributionsViewDto? _habitView;
    private string _selectedPeriodKey = DailyGraphPeriods.Rolling370Days;
    private string _tagFilter = "";
    private IReadOnlyCollection<string> _selectedTags = Array.Empty<string>();
    private bool _loadingDailies;
    private string? _dailyError;
    private bool _loadingHabits;
    private string? _habitError;
    private int _bestStreakDays;
    private string? _bestStreakTitle;
    private bool _shouldScrollToEnd;
    private DateOnly _heatmapToday;

    private const int InitialVisibleConsistencyCount = 6;
    private int _visibleConsistencyCount = InitialVisibleConsistencyCount;
    private int _loadVersion;
    private bool _disposed;
    private readonly Dictionary<Guid, (string Label, string Tooltip)> _dailyRatioCache = [];

    [Inject] public IServiceProvider ServiceProvider { get; set; } = default!;

    private void LogFallback(Exception ex, string message) =>
        ServiceProvider.GetService<ILogger<StatisticsPanel>>()?.LogDebug(ex, "{Message}", message);

    private OfflineActivityStatisticsProvider? OfflineStats => ServiceProvider.GetService<OfflineActivityStatisticsProvider>();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_shouldScrollToEnd)
        {
            _shouldScrollToEnd = false;
            try
            {
                // Ensure the helper exists first. On cold load this render can win the race
                // against the page script loader, and a missed snap is never retried.
                // The loader dedupes, so this call is free once loaded.
                await JSRuntime.InvokeVoidAsync("habitinatorLoadScript", "_content/App.Shared.RCL/js/statisticsScrolling.js");
                await JSRuntime.InvokeVoidAsync("scrollHeatmapsToEnd");
                await JSRuntime.InvokeVoidAsync("initializeHeatmapRovingTabindex");
            }
            catch (Exception)
            {
                // Ignore JS errors if components disappear or JS isn't ready
            }
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _subscription = ApplicationState.RegisterOnPersisting(PersistStatisticsData);
        _heatmapToday = DailySchedule.LocalToday(TimeZoneService);
        if (TryRestoreInitialOverview())
        {
            _loading = false;
            _loadingDailies = false;
            _loadingHabits = false;
        }

        try
        {
            await DateFormatService.InitializeAsync();
            await LoadStatisticsAsync(_selectedPeriodKey);
        }
        catch (Exception ex)
        {
            await HandleInitErrorAsync(ex);
        }
        finally
        {
            _loadingDailies = false;
            _loadingHabits = false;
            _loading = false;
        }
    }

    private bool TryRestoreInitialOverview()
    {
        if (ApplicationState.TryTakeFromJson<ActivityOverviewDto>("stats_overview_data", out var restoredOverview) && restoredOverview is not null)
        {
            ApplyOverview(restoredOverview);
            return true;
        }

        if (Stats.TryGetCachedOverview(_selectedPeriodKey, string.IsNullOrEmpty(_tagFilter) ? null : _tagFilter, out var cached) && cached is not null)
        {
            ApplyOverview(cached);
            return true;
        }

        return false;
    }

    private async Task HandleInitErrorAsync(Exception ex)
    {
        if (_data != null)
        {
            await SafeNotifyAsync("Offline: showing last available stats.", Severity.Warning);
            return;
        }

        if (await TryApplyOfflineOverviewAsync(_selectedPeriodKey, _tagFilter))
        {
            await SafeNotifyAsync("Offline: showing locally computed stats.", Severity.Warning);
            return;
        }

        _error = ex.Message;
        await SafeNotifyAsync("Could not load statistics. Please try again.", Severity.Error);
    }

    private async Task<bool> TryApplyOfflineOverviewAsync(string periodKey, string? tagFilter, Task<Dictionary<Guid, int>>? streaksTask = null)
    {
        if (OfflineStats == null)
        {
            return false;
        }

        try
        {
            var tag = string.IsNullOrEmpty(tagFilter) ? null : tagFilter;
            var fallback = await OfflineStats.BuildOverviewAsync(periodKey, tag);
            ApplyOverview(fallback);
            if (streaksTask != null)
            {
                await ApplyBestStreakAsync(streaksTask);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task SafeNotifyAsync(string message, Severity severity)
    {
        try
        {
            await Notifier.NotifyAsync(message, severity);
        }
        catch (Exception notifyEx)
        {
            LogFallback(notifyEx, "Best-effort toast failed; fallback already rendered.");
        }
    }

    private Task PersistStatisticsData()
    {
        if (_data is not null && _dailyView is not null && _habitView is not null)
        {
            var overview = new ActivityOverviewDto(_data, _dailyView, _habitView);
            ApplicationState.PersistAsJson("stats_overview_data", overview);
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _disposed = true;
        _loadVersion++;
        _subscription.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ApplyOverview(ActivityOverviewDto overview)
    {
        _data = overview.Dashboard;
        _dailyView = overview.DailyContributions;
        _habitView = overview.HabitContributions;

        _selectedPeriodKey = _data.PeriodKey;
        PruneTagFilter(_data.AvailableTags);

        _periodOptions = _dailyView.PeriodOptions;
        _cellIndex = _data.Heatmap.ToDictionary(x => (x.DayRow, x.WeekCol));
        _dailyCellIndices.Clear();
        _habitCellIndices.Clear();
        _dailyRatioCache.Clear();
        _weekBarMax = _data.WeekBars.Count == 0
            ? 0
            : _data.WeekBars.Max(x => x.EventCount);
        _shouldScrollToEnd = true;
    }

    private void ApplyDashboard(ActivityDashboardDto dashboard)
    {
        _data = dashboard;
        _selectedPeriodKey = dashboard.PeriodKey;
        PruneTagFilter(dashboard.AvailableTags);

        _cellIndex = dashboard.Heatmap.ToDictionary(x => (x.DayRow, x.WeekCol));
        _weekBarMax = dashboard.WeekBars.Count == 0
            ? 0
            : dashboard.WeekBars.Max(x => x.EventCount);
        _shouldScrollToEnd = true;
    }

    private void ApplyDailyContributions(DailyContributionsViewDto dailyView)
    {
        _dailyView = dailyView;
        _periodOptions = dailyView.PeriodOptions;
        _selectedPeriodKey = dailyView.PeriodKey;
        _dailyCellIndices.Clear();
        _dailyRatioCache.Clear();
        // Fresh grids mount with this paint. Cold load renders skeletons first, so snap them too.
        _shouldScrollToEnd = true;
    }

    private void ApplyHabitContributions(HabitContributionsViewDto habitView)
    {
        _habitView = habitView;
        _periodOptions ??= habitView.PeriodOptions;
        _habitCellIndices.Clear();
        // Fresh grids mount with this paint. Cold load renders skeletons first, so snap them too.
        _shouldScrollToEnd = true;
    }

    private void PruneTagFilter(IReadOnlyList<string>? tagList)
    {
        tagList ??= [];
        if (!string.IsNullOrEmpty(_tagFilter))
        {
            var splitFilter = _tagFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var validTags = splitFilter.Where(t => tagList.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
            _selectedTags = validTags;
            _tagFilter = string.Join(",", _selectedTags);
        }
        else
        {
            _selectedTags = Array.Empty<string>();
            _tagFilter = "";
        }
    }

    private async Task LoadStatisticsAsync(string periodKey)
    {
        var version = ++_loadVersion;

        _heatmapToday = DailySchedule.LocalToday(TimeZoneService);
        _visibleConsistencyCount = InitialVisibleConsistencyCount;
        if (_dailyView is null)
        {
            _loadingDailies = true;
        }

        _dailyError = null;
        if (_habitView is null)
        {
            _loadingHabits = true;
        }

        _habitError = null;
        var tag = string.IsNullOrEmpty(_tagFilter) ? null : _tagFilter;
        var streaksTask = BoardData.GetStreakMapAsync(CancellationToken.None);

        // A cached overview paints the hero and all grids instantly. The load below
        // then revalidates over the network so a check-in never paints stale.
        if (Stats.TryGetCachedOverview(periodKey, tag, out var cachedOverview) && cachedOverview is not null)
        {
            ApplyOverview(cachedOverview);
            _loading = false;
            _loadingDailies = false;
            _loadingHabits = false;
            await ApplyBestStreakAsync(streaksTask);
            await InvokeAsync(StateHasChanged);
        }

        // A cached dashboard with cached contributions paints instantly. The same revalidation runs below.
        else if (Stats.TryGetCachedDashboard(periodKey, tag, out var cachedDashboard) && cachedDashboard is not null &&
            Stats.TryGetCachedDailyContributions(periodKey, tag, out var cachedDaily) && cachedDaily is not null &&
            Stats.TryGetCachedHabitContributions(periodKey, tag, out var cachedHabit) && cachedHabit is not null)
        {
            ApplyDashboard(cachedDashboard);
            ApplyDailyContributions(cachedDaily);
            ApplyHabitContributions(cachedHabit);
            _loading = false;
            _loadingDailies = false;
            _loadingHabits = false;
            await ApplyBestStreakAsync(streaksTask);
            await InvokeAsync(StateHasChanged);
        }

        try
        {
            // Fire all three reads at once so the grids do not wait an extra round trip
            // behind the dashboard. The hero still paints first for progressive load.
            var dashboardTask = ResolveDashboardAsync(periodKey, tag);
            var dailyTask = Stats.GetDailyContributionsAsync(periodKey, tag, CancellationToken.None);
            var habitTask = Stats.GetHabitContributionsAsync(periodKey, tag, CancellationToken.None);

            ActivityDashboardDto dashboard;
            try
            {
                dashboard = await dashboardTask;
            }
            catch
            {
                // The sibling reads are no longer needed on this path. Observe their
                // faults so failures do not go unobserved.
                ObserveFaults(dailyTask, habitTask);
                throw;
            }

            if (version != _loadVersion || _disposed)
            {
                ObserveFaults(dailyTask, habitTask);
                return;
            }

            ApplyDashboard(dashboard);
            _loading = false;
            await InvokeAsync(StateHasChanged);

            try
            {
                var daily = await dailyTask;
                if (version == _loadVersion && !_disposed)
                {
                    ApplyDailyContributions(daily);
                }
            }
            catch (Exception dex)
            {
                if (version == _loadVersion)
                {
                    _dailyError = dex.Message;
                    LogFallback(dex, "Daily contributions failed. The hero still shows.");
                }
            }
            finally
            {
                if (version == _loadVersion)
                {
                    _loadingDailies = false;
                }
            }

            try
            {
                var habit = await habitTask;
                if (version == _loadVersion && !_disposed)
                {
                    ApplyHabitContributions(habit);
                }
            }
            catch (Exception hex)
            {
                if (version == _loadVersion)
                {
                    _habitError = hex.Message;
                    LogFallback(hex, "Habit contributions failed. The hero still shows.");
                }
            }
            finally
            {
                if (version == _loadVersion)
                {
                    _loadingHabits = false;
                }
            }

            if (version != _loadVersion || _disposed)
            {
                return;
            }

            await InvokeAsync(StateHasChanged);
            await ApplyBestStreakAsync(streaksTask);
        }
        catch (Exception dex)
        {
            if (version == _loadVersion)
            {
                await HandleLoadErrorAsync(dex, periodKey, tag, streaksTask);
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                _loadingDailies = false;
                _loadingHabits = false;
            }
        }
    }

    private async Task<ActivityDashboardDto> ResolveDashboardAsync(string periodKey, string? tag)
    {
        if (ApplicationState.TryTakeFromJson<ActivityOverviewDto>("stats_overview_data", out var restoredOverview) && restoredOverview?.Dashboard is not null)
        {
            if (restoredOverview.DailyContributions is not null)
            {
                ApplyDailyContributions(restoredOverview.DailyContributions);
                _loadingDailies = false;
            }

            if (restoredOverview.HabitContributions is not null)
            {
                ApplyHabitContributions(restoredOverview.HabitContributions);
                _loadingHabits = false;
            }

            return restoredOverview.Dashboard;
        }

        if (ApplicationState.TryTakeFromJson<ActivityDashboardDto>("stats_dashboard_data", out var d) && d is not null)
        {
            if (ApplicationState.TryTakeFromJson<DailyContributionsViewDto>("stats_daily_view_data", out var dv) && dv is not null)
            {
                ApplyDailyContributions(dv);
                _loadingDailies = false;
            }

            if (ApplicationState.TryTakeFromJson<HabitContributionsViewDto>("stats_habit_view_data", out var hv) && hv is not null)
            {
                ApplyHabitContributions(hv);
                _loadingHabits = false;
            }

            return d;
        }

        if (Stats.TryGetCachedDashboard(periodKey, tag, out var cached) && cached is not null)
        {
            if (Stats.TryGetCachedDailyContributions(periodKey, tag, out var cachedDaily) && cachedDaily is not null)
            {
                ApplyDailyContributions(cachedDaily);
                _loadingDailies = false;
            }

            if (Stats.TryGetCachedHabitContributions(periodKey, tag, out var cachedHabit) && cachedHabit is not null)
            {
                ApplyHabitContributions(cachedHabit);
                _loadingHabits = false;
            }

            return cached;
        }

        return await Stats.GetDashboardAsync(periodKey, tag);
    }

    private async Task<ActivityOverviewDto> ResolveOverviewAsync(string periodKey, string? tag)
    {
        if (ApplicationState.TryTakeFromJson<ActivityOverviewDto>("stats_overview_data", out var restoredOverview) && restoredOverview is not null)
        {
            return restoredOverview;
        }

        if (ApplicationState.TryTakeFromJson<ActivityDashboardDto>("stats_dashboard_data", out var d) &&
            ApplicationState.TryTakeFromJson<DailyContributionsViewDto>("stats_daily_view_data", out var dv) &&
            ApplicationState.TryTakeFromJson<HabitContributionsViewDto>("stats_habit_view_data", out var hv) &&
            d is not null && dv is not null && hv is not null)
        {
            return new ActivityOverviewDto(d, dv, hv);
        }

        return await Stats.GetOverviewAsync(periodKey, tag);
    }

    private async Task HandleLoadErrorAsync(Exception dex, string periodKey, string? tag, Task<Dictionary<Guid, int>> streaksTask)
    {
        if (await TryApplyOfflineOverviewAsync(periodKey, tag, streaksTask))
        {
            await SafeNotifyAsync("Offline: showing locally computed stats.", Severity.Warning);
            return;
        }

        if (_data != null)
        {
            await SafeNotifyAsync("Could not refresh statistics. Showing last available data.", Severity.Warning);
            return;
        }

        _error = dex.Message;
        _dailyError = dex.Message;
        _habitError = dex.Message;
        _data = null;
        _dailyView = null;
        _habitView = null;
        _periodOptions = null;
        _cellIndex = [];
        _dailyCellIndices.Clear();
        _habitCellIndices.Clear();
        _dailyRatioCache.Clear();
        await SafeNotifyAsync("Could not load statistics. Please try again.", Severity.Error);
    }

    private Dictionary<(int Row, int Col), ActivityHeatmapCellDto> GetDailyCellIndex(DailyContributionGraphDto daily)
    {
        if (_dailyCellIndices.TryGetValue(daily.BoardItemId, out var map))
        {
            return map;
        }

        map = daily.Heatmap.ToDictionary(x => (x.DayRow, x.WeekCol));
        _dailyCellIndices[daily.BoardItemId] = map;
        return map;
    }

    private Dictionary<(int Row, int Col), ActivityHeatmapCellDto> GetHabitCellIndex(HabitContributionGraphDto habit)
    {
        if (_habitCellIndices.TryGetValue(habit.BoardItemId, out var map))
        {
            return map;
        }

        map = habit.Heatmap.ToDictionary(x => (x.DayRow, x.WeekCol));
        _habitCellIndices[habit.BoardItemId] = map;
        return map;
    }

    private async Task ApplyBestStreakAsync(Task<Dictionary<Guid, int>> streaksTask)
    {
        try
        {
            var streaks = await streaksTask;
            _bestStreakDays = 0;
            _bestStreakTitle = null;
            if (_dailyView is not null)
            {
                foreach (var graph in _dailyView.Graphs)
                {
                    var longest = graph.LongestStreak;
                    if (longest <= 0 && streaks.TryGetValue(graph.BoardItemId, out var current))
                    {
                        longest = current;
                    }

                    if (longest > _bestStreakDays)
                    {
                        _bestStreakDays = longest;
                        _bestStreakTitle = graph.Title;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignored. The KPI simply stays empty
        }
    }

    private Dictionary<string, object> GetConsistencyTabAttrs(string tab) => new()
    {
        ["aria-pressed"] = _consistencyTab == tab ? "true" : "false"
    };

    private static string HabitActiveRatioLabel(int activeDays, int periodDays)
    {
        var percent = periodDays <= 0 ? 0 : (int)Math.Round(100.0 * activeDays / periodDays);
        return $"{activeDays} of {periodDays} days, {percent}%";
    }

    private (string Label, string Tooltip) GetDailyRatio(DailyContributionGraphDto daily)
    {
        if (_dailyRatioCache.TryGetValue(daily.BoardItemId, out var cached))
        {
            return cached;
        }

        var active = 0;
        var total = 0;
        foreach (var c in daily.Heatmap)
        {
            if (!c.InDataRange)
            {
                continue;
            }

            total++;
            if (c.Count > 0)
            {
                active++;
            }
        }

        var percent = total <= 0 ? 0 : (int)Math.Round(100.0 * active / total);
        var label = $"{active} of {total} days, {percent}%";
        var dayWord = active == 1 ? "day" : "days";
        var tooltip = $"{active} active {dayWord} of {total} in this period";
        var result = (label, tooltip);
        _dailyRatioCache[daily.BoardItemId] = result;
        return result;
    }

    private string GetDailyRatioLabel(DailyContributionGraphDto daily) => GetDailyRatio(daily).Label;

    private string GetDailyRatioTooltip(DailyContributionGraphDto daily) => GetDailyRatio(daily).Tooltip;

    private static string DailyActiveRatioLabel(DailyContributionGraphDto daily)
    {
        var active = daily.Heatmap.Count(c => c.InDataRange && c.Count > 0);
        var total = daily.Heatmap.Count(c => c.InDataRange);
        var percent = total <= 0 ? 0 : (int)Math.Round(100.0 * active / total);
        return $"{active} of {total} days, {percent}%";
    }

    private static string DailyActiveRatioTooltip(DailyContributionGraphDto daily)
    {
        var active = daily.Heatmap.Count(c => c.InDataRange && c.Count > 0);
        var total = daily.Heatmap.Count(c => c.InDataRange);
        var label = active == 1 ? "day" : "days";
        return $"{active} active {label} of {total} in this period";
    }

    private void ShowAllConsistency()
    {
        _visibleConsistencyCount = int.MaxValue;
    }

    private string ActivityHeatmapCellClass(ActivityHeatmapCellDto cell)
    {
        return $"stats-heatmap-day-btn stats-cell stats-lvl-{cell.Intensity}{(IsHeatmapToday(cell.Date) ? " stats-heatmap-day--today" : "")}";
    }

    private string DailyHeatmapCellClass(ActivityHeatmapCellDto cell)
    {
        var cls = $"stats-cell stats-lvl-{cell.Intensity}{(IsHeatmapToday(cell.Date) ? " stats-heatmap-day--today" : "")}";
        if (cell.InDataRange && cell.Due)
        {
            cls += " stats-daily-due";
        }

        return cls;
    }

    private string HabitHeatmapCellClass(ActivityHeatmapCellDto cell)
    {
        return $"stats-cell stats-lvl-{cell.Intensity}{(IsHeatmapToday(cell.Date) ? " stats-heatmap-day--today" : "")}";
    }

    private string TitleWithTodayPrefix(DateOnly date, string baseTitle)
    {
        return IsHeatmapToday(date) ? $"Today, {baseTitle}" : baseTitle;
    }

    private string HabitHeatmapDayTitle(DateOnly date, int count)
    {
        var logs = count == 1 ? "1 log" : $"{count} logs";
        var baseTitle = $"{DateFormatService.Format(date)}: {logs} - view details";
        return TitleWithTodayPrefix(date, baseTitle);
    }

    private async Task OnStatsPeriodChanged(string? periodKey)
    {
        var newPeriod = string.IsNullOrEmpty(periodKey) ? DailyGraphPeriods.Rolling370Days : periodKey;
        if (newPeriod == _selectedPeriodKey)
        {
            return;
        }

        _selectedPeriodKey = newPeriod;
        await RefreshAsync(() => LoadStatisticsAsync(_selectedPeriodKey));
    }


    private static string GetMultiSelectionText(IReadOnlyList<string> selectedValues)
    {
        if (selectedValues is null || selectedValues.Count == 0)
        {
            return "All tags";
        }

        if (selectedValues.Count == 1)
        {
            return selectedValues[0];
        }

        if (selectedValues.Count == 2)
        {
            return $"{selectedValues[0]}, {selectedValues[1]}";
        }

        return $"{selectedValues.Count} tags selected";
    }

    private async Task OnSelectedTagsChanged(IReadOnlyCollection<string> values)
    {
        var newTags = values ?? Array.Empty<string>();
        if (newTags.Count == _selectedTags.Count && newTags.All(_selectedTags.Contains))
        {
            return;
        }

        _selectedTags = newTags;
        _tagFilter = string.Join(",", _selectedTags);
        await RefreshAsync(() => LoadStatisticsAsync(_selectedPeriodKey));
    }

    private async Task OnRemoveTagFilterAsync(string tag)
    {
        var newTags = _selectedTags.Where(t => !string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)).ToList();
        await OnSelectedTagsChanged(newTags);
    }

    private async Task OnClearTagFiltersAsync()
    {
        await OnSelectedTagsChanged(Array.Empty<string>());
    }

    private async Task RefreshAsync(Func<Task> load)
    {
        if (_loading)
        {
            return;
        }

        _periodBusy = true;
        _error = null;
        _dailyError = null;
        try
        {
            await load();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            try
            {
                await Notifier.NotifyAsync("Could not refresh statistics. Please try again.", Severity.Error);
            }
            catch (Exception notifyEx)
            {
                LogFallback(notifyEx, "Best-effort toast failed; fallback already rendered.");
            }
        }
        finally
        {
            _periodBusy = false;
        }
    }

    private async Task OpenDayDetailAsync(DateOnly date)
    {
        var options = DialogDefaults.Wide;
        var filterTag = string.IsNullOrEmpty(_tagFilter) ? null : _tagFilter;
        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, date },
            { x => x.TagFilter, filterTag }
        };
        var dialog = await DialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters, options);
        _ = await dialog.Result;
        await RefreshAfterDialogAsync();
    }

    private async Task OpenDailyHeatmapAsync(DailyContributionGraphDto daily)
    {
        var parameters = new DialogParameters<DailyHeatmapDialog>
        {
            { x => x.BoardItemId, daily.BoardItemId },
            { x => x.Title, daily.Title }
        };
        var dialog = await DialogService.ShowAsync<DailyHeatmapDialog>(string.Empty, parameters, DialogDefaults.Wide);
        _ = await dialog.Result;
        await RefreshAfterDialogAsync();
    }

    private async Task OpenDailyDayDetailAsync(DailyContributionGraphDto daily, int row, int col)
    {
        if (!GetDailyCellIndex(daily).TryGetValue((row, col), out var cell) || !cell.InDataRange)
        {
            return;
        }

        var parameters = new DialogParameters<ActivityDayDetailDialog>
        {
            { x => x.Date, cell.Date },
            { x => x.BoardItemId, daily.BoardItemId }
        };
        var dialog = await DialogService.ShowAsync<ActivityDayDetailDialog>(string.Empty, parameters, DialogDefaults.Wide);
        _ = await dialog.Result;
        await RefreshAfterDialogAsync();
    }

    /// <summary>
    ///     Reloads stats after a stats dialog closes so heatmaps show retro check-ins.
    ///     The dialog can report Cancel with no mutation flag when dismissed by clicking away.
    ///     Drain and invalidate first so the reload reads the synced state.
    /// </summary>
    private async Task RefreshAfterDialogAsync()
    {
        await DrainBoardSyncAsync();
        try
        {
            Stats.InvalidateCache();
        }
        catch
        {
            // Best-effort cleanup. A stale cache entry makes the next load retry.
        }

        await RefreshAsync(() => LoadStatisticsAsync(_selectedPeriodKey));
    }

    /// <summary>Marks already-started reads as observed so a fault never escapes unobserved when their results are discarded on a stale load or a failed sibling.</summary>
    private static void ObserveFaults(params Task[] tasks)
    {
        foreach (var t in tasks)
        {
            _ = t.ContinueWith(
                static inner => _ = inner.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    /// <summary>Tries an immediate sync. Stats read from the server right after.</summary>
    private async Task DrainBoardSyncAsync()
    {
        try
        {
            var sync = ServiceProvider.GetService<App.Shared.RCL.Services.Board.Local.IBoardSyncRequestor>();
            if (sync is null)
            {
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await sync.SyncNowAsync(cts.Token);
        }
        catch
        {
            // If offline or slow, the periodic sync delivers the change. Reads use last known data.
        }
    }

    private static string GetDailyStreakTooltip(DailyContributionGraphDto daily) =>
        $"Current streak: {daily.CurrentStreak} days. Previous streak: {daily.PreviousStreak} days. Longest streak: {daily.LongestStreak} days.";

    private static string FormatBusiestDayDetail(DateOnly day, int eventCount)
    {
        var weekday = day.ToString("dddd", CultureInfo.InvariantCulture);
        var eventsLabel = eventCount == 1 ? "1 event" : $"{eventCount} events";
        return $"{weekday}, {eventsLabel}";
    }

    private static string FormatFocus(int totalMinutes)
    {
        if (totalMinutes <= 0)
        {
            return "-";
        }

        if (totalMinutes < 60)
        {
            return $"{totalMinutes} min";
        }

        return $"{totalMinutes / 60}h {totalMinutes % 60}m";
    }

    private string FormatRange(DateOnly from, DateOnly to) => $"{DateFormatService.Format(from)} - {DateFormatService.Format(to)}";

    private static string GetWeeklyDeltaClass(int percentChange) =>
        percentChange >= 0 ? "stats-weekly-tile__delta stats-weekly-tile__delta--up" : "stats-weekly-tile__delta stats-weekly-tile__delta--down";

    private string FormatWeekBarTooltip(ActivityWeekBarDto w) =>
        $"{DateFormatService.Format(w.WeekStart)} week: {w.EventCount} events, {w.FocusMinutes} min focus";

    private async Task ShowWeekBarValueAsync(ActivityWeekBarDto w)
    {
        try
        {
            await Notifier.NotifyAsync(FormatWeekBarTooltip(w), Severity.Info);
        }
        catch
        {
            // Shows a best-effort toast. The chart still shows the value.
        }
    }

    private static string FormatHabitRatioTooltip(int activeDayCount, int periodDayCount)
    {
        var label = activeDayCount == 1 ? "day" : "days";
        return $"{activeDayCount} active {label} of {periodDayCount} in this period";
    }

    private bool IsHeatmapToday(DateOnly date) => date == _heatmapToday;

    private string HeatmapDayTitle(DateOnly date, int count)
    {
        var baseTitle = $"{DateFormatService.Format(date)}: {count} events - view details";
        return TitleWithTodayPrefix(date, baseTitle);
    }

    private string DailyHeatmapDayTitle(ActivityHeatmapCellDto cell)
    {
        if (cell.Count > 0)
        {
            var done = $"{cell.Count} complete(s)";
            var doneTitle = cell.Due ? done : $"{done}, not due";
            return TitleWithTodayPrefix(cell.Date, $"{DateFormatService.Format(cell.Date)}: {doneTitle}");
        }

        var status = cell.Due ? "due, not done" : "not due";
        return TitleWithTodayPrefix(cell.Date, $"{DateFormatService.Format(cell.Date)}: {status}");
    }

    private sealed record KpiDescriptor(
        string CardAccent,
        string Icon,
        string CardLabel,
        Func<string> CardValue,
        Func<string> CardDetail);

    private KpiDescriptor[] Kpis(ActivityDashboardDto data) =>
    [
        CreateEventsKpi(data),
        CreateFocusKpi(data),
        CreatePeakKpi(data),
        CreateStreakKpi()
    ];

    private static KpiDescriptor CreateEventsKpi(ActivityDashboardDto data)
    {
        var count = data.TotalEvents.ToString(CultureInfo.InvariantCulture);
        return new KpiDescriptor(
            "stats-kpi-card--events",
            Icons.Material.Filled.ViewTimeline,
            "Total events",
            () => count,
            () => "Logged in this period");
    }

    private static KpiDescriptor CreateFocusKpi(ActivityDashboardDto data)
    {
        var formatted = FormatFocus(data.TotalFocusMinutes);
        return new KpiDescriptor(
            "stats-kpi-card--focus",
            Icons.Material.Filled.Timer,
            "Focus time",
            () => formatted,
            () => "From focus timer sessions");
    }

    private KpiDescriptor CreatePeakKpi(ActivityDashboardDto data)
    {
        var busiestDay = data.BusiestDay.GetValueOrDefault();
        var hasPeak = data.BusiestDay.HasValue && data.MaxDayCount > 0;
        return new KpiDescriptor(
            "stats-kpi-card--peak",
            Icons.Material.Filled.TrendingUp,
            "Busiest day",
            () => hasPeak ? DateFormatService.Format(busiestDay) : "-",
            () => hasPeak ? FormatBusiestDayDetail(busiestDay, data.MaxDayCount) : "No activity in range");
    }

    private KpiDescriptor CreateStreakKpi()
    {
        var hasStreak = _bestStreakDays > 0;
        var streakDays = _bestStreakDays;
        var streakTitle = _bestStreakTitle;

        return new KpiDescriptor(
            "stats-kpi-card--streak",
            Icons.Material.Filled.Whatshot,
            "Longest streak",
            () => hasStreak ? $"{streakDays} days" : "-",
            () => hasStreak ? streakTitle ?? "" : "No active streaks yet");
    }

}
