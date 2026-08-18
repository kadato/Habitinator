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

    void InvalidateCache()
    {
    }
}
