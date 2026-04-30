using App.Shared.RCL.Models;
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

    public async Task LogActivityAsync(
        ActivityEventType eventType,
        Guid? boardItemId,
        int? durationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true) return;

        var userId = await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        await _persistence.LogActivityAsync(userId, eventType, boardItemId, durationSeconds, cancellationToken);
    }

    public async Task LogTimerSessionAsync(TimeSpan duration, Guid? boardItemId, string? customLabel = null,
        CancellationToken cancellationToken = default)
    {
        var sec = (int)Math.Min(int.MaxValue, Math.Max(0, duration.TotalSeconds));
        if (sec == 0) return;

        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true) return;

        var userId = await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        await _persistence.LogTimerSessionAsync(userId, duration, boardItemId, customLabel, cancellationToken);
    }
}
