using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebActivityStatisticsReader : IActivityStatisticsReader
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly CurrentUserAccessor _currentUserAccessor;
    private readonly ActivityStatisticsService _statisticsService;

    public WebActivityStatisticsReader(
        AuthenticationStateProvider authenticationStateProvider,
        CurrentUserAccessor currentUserAccessor,
        ActivityStatisticsService statisticsService)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _currentUserAccessor = currentUserAccessor;
        _statisticsService = statisticsService;
    }

    public async Task<ActivityDashboardDto> GetDashboardAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await RequireUserIdAsync(cancellationToken);
        return await _statisticsService.GetDashboardAsync(userId, periodKey, tag, cancellationToken);
    }

    public async Task<DailyContributionsViewDto> GetDailyContributionsAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await RequireUserIdAsync(cancellationToken);
        return await _statisticsService.GetDailyContributionsAsync(userId, periodKey, tag, cancellationToken);
    }

    public async Task<HabitContributionsViewDto> GetHabitContributionsAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await RequireUserIdAsync(cancellationToken);
        return await _statisticsService.GetHabitContributionsAsync(userId, periodKey, tag, cancellationToken);
    }

    public async Task<ActivityDayDetailDto> GetActivityDayDetailAsync(DateOnly day, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await RequireUserIdAsync(cancellationToken);
        return await _statisticsService.GetActivityDayDetailAsync(userId, day, tag, cancellationToken);
    }

    private async Task<Guid> RequireUserIdAsync(CancellationToken cancellationToken)
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        return await _currentUserAccessor.ResolveAsync(state.User, cancellationToken);
    }
}
