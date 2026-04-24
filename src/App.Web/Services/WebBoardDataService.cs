using System.Security.Claims;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebBoardDataService : IBoardDataService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly DemoUserResolver _demoUserResolver;
    private readonly BoardPersistenceService _boardPersistenceService;

    public WebBoardDataService(
        AuthenticationStateProvider authenticationStateProvider,
        DemoUserResolver demoUserResolver,
        BoardPersistenceService boardPersistenceService)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _demoUserResolver = demoUserResolver;
        _boardPersistenceService = boardPersistenceService;
    }

    public async Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.GetSnapshotAsync(userId, cancellationToken);
    }

    public async Task<BoardItem> CreateItemAsync(BoardSection section, string title, CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.CreateItemAsync(userId, section, title, cancellationToken);
    }

    public async Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title, CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.RenameItemAsync(userId, section, itemId, title, cancellationToken);
    }

    public async Task<bool> DeleteItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.DeleteItemAsync(userId, section, itemId, cancellationToken);
    }

    public async Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.ToggleItemAsync(userId, section, itemId, cancellationToken);
    }

    public async Task<BoardItem?> IncrementHabitAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.IncrementHabitAsync(userId, itemId, cancellationToken);
    }

    public async Task<BoardItem?> DecrementHabitAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.DecrementHabitAsync(userId, itemId, cancellationToken);
    }

    private async Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        AuthenticationState authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        ClaimsPrincipal user = authState.User;
        return await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
    }
}
