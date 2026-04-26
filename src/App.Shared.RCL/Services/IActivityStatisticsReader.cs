namespace App.Shared.RCL.Services;

public interface IActivityStatisticsReader
{
    Task<ActivityDashboardDto> GetDashboardAsync(string? periodKey, CancellationToken cancellationToken = default);

    Task<DailyContributionsViewDto> GetDailyContributionsAsync(string? periodKey,
        CancellationToken cancellationToken = default);

    Task<ActivityDayDetailDto> GetActivityDayDetailAsync(DateOnly day, CancellationToken cancellationToken = default);
}
