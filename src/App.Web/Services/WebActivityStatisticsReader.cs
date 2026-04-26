using System.Security.Claims;
using App.Shared.RCL.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebActivityStatisticsReader : IActivityStatisticsReader
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly DemoUserResolver _demoUserResolver;
    private readonly ActivityStatisticsService _statisticsService;

    public WebActivityStatisticsReader(
        AuthenticationStateProvider authenticationStateProvider,
        DemoUserResolver demoUserResolver,
        ActivityStatisticsService statisticsService)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _demoUserResolver = demoUserResolver;
        _statisticsService = statisticsService;
    }

    public async Task<ActivityDashboardDto> GetDashboardAsync(string? periodKey, CancellationToken cancellationToken = default)
    {
        Guid userId = await RequireUserIdAsync(cancellationToken);
        return await _statisticsService.GetDashboardAsync(userId, periodKey, cancellationToken);
    }

    public async Task<DailyContributionsViewDto> GetDailyContributionsAsync(string? periodKey, CancellationToken cancellationToken = default)
    {
        Guid userId = await RequireUserIdAsync(cancellationToken);
        return await _statisticsService.GetDailyContributionsAsync(userId, periodKey, cancellationToken);
    }

    public async Task<ActivityDayDetailDto> GetActivityDayDetailAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        Guid userId = await RequireUserIdAsync(cancellationToken);
        return await _statisticsService.GetActivityDayDetailAsync(userId, day, cancellationToken);
    }

    private async Task<Guid> RequireUserIdAsync(CancellationToken cancellationToken)
    {
        AuthenticationState state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        ClaimsPrincipal user = state.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("Sign in required.");
        }

        return await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
    }
}
