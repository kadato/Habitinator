using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public interface IActivityStatisticsReader
{
    Task<ActivityDashboardDto> GetDashboardAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default);

    Task<DailyContributionsViewDto> GetDailyContributionsAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default);

    Task<HabitContributionsViewDto> GetHabitContributionsAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default);

    Task<ActivityOverviewDto> GetOverviewAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default);

    Task<ActivityDayDetailDto> GetActivityDayDetailAsync(DateOnly day, string? tag = null,
        CancellationToken cancellationToken = default);

    bool TryGetCachedOverview(string? periodKey, string? tag, out ActivityOverviewDto? overview)
    {
        overview = null;
        return false;
    }

    bool TryGetCachedDashboard(string? periodKey, string? tag, out ActivityDashboardDto? dashboard)
    {
        dashboard = null;
        return false;
    }

    bool TryGetCachedDailyContributions(string? periodKey, string? tag, out DailyContributionsViewDto? view)
    {
        view = null;
        return false;
    }

    bool TryGetCachedHabitContributions(string? periodKey, string? tag, out HabitContributionsViewDto? view)
    {
        view = null;
        return false;
    }

    void InvalidateCache()
    {
    }

    void InvalidateForTags(IEnumerable<string>? tags)
    {
        InvalidateCache();
    }

    void InvalidateForItem(BoardItem item)
    {
        InvalidateForTags(BoardTagUtil.ParseTags(item.Tags));
    }
}
