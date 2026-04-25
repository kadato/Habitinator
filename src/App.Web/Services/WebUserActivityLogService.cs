using System.Security.Claims;
using App.Shared.RCL.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebUserActivityLogService : IUserActivityLogService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly DemoUserResolver _demoUserResolver;
    private readonly BoardPersistenceService _persistence;

    public WebUserActivityLogService(
        AuthenticationStateProvider authenticationStateProvider,
        DemoUserResolver demoUserResolver,
        BoardPersistenceService boardPersistence)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _demoUserResolver = demoUserResolver;
        _persistence = boardPersistence;
    }

    public async Task LogTimerSessionAsync(TimeSpan duration, Guid? boardItemId, CancellationToken cancellationToken = default)
    {
        AuthenticationState state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        ClaimsPrincipal user = state.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return;
        }

        Guid userId = await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        await _persistence.LogTimerSessionAsync(userId, duration, boardItemId, cancellationToken);
    }
}
