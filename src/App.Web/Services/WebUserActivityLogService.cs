using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebUserActivityLogService : IUserActivityLogService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly CurrentUserAccessor _currentUserAccessor;
    private readonly BoardPersistenceService _persistence;

    public WebUserActivityLogService(
        AuthenticationStateProvider authenticationStateProvider,
        CurrentUserAccessor currentUserAccessor,
        BoardPersistenceService boardPersistence)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _currentUserAccessor = currentUserAccessor;
        _persistence = boardPersistence;
    }

    public async Task LogActivityAsync(
        ActivityEventType eventType,
        Guid? boardItemId,
        int? durationSeconds = null,
        string? itemTitleSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = await _currentUserAccessor.TryResolveAsync(state.User, cancellationToken);
        if (userId is null)
        {
            return;
        }

        await _persistence.LogActivityAsync(
            userId.Value,
            eventType,
            boardItemId,
            durationSeconds,
            itemTitleSnapshot,
            cancellationToken);
    }

    public async Task LogTimerSessionAsync(TimeSpan duration, Guid? boardItemId, string? customLabel = null,
        CancellationToken cancellationToken = default)
    {
        var sec = BoardPersistenceService.DurationSeconds(duration);
        if (sec == 0)
        {
            return;
        }

        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = await _currentUserAccessor.TryResolveAsync(state.User, cancellationToken);
        if (userId is null)
        {
            return;
        }

        await _persistence.LogTimerSessionAsync(userId.Value, duration, boardItemId, customLabel, cancellationToken);
    }
}
