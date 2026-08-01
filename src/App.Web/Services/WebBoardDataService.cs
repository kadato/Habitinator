using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebBoardDataService(
    AuthenticationStateProvider authenticationStateProvider,
    DemoUserResolver demoUserResolver,
    BoardPersistenceService boardPersistenceService) : IBoardDataService
{

    public async Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.GetSnapshotAsync(userId, cancellationToken);
    }

    public async Task<BoardItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.GetItemAsync(userId, itemId, cancellationToken);
    }

    public async Task<BoardItem> CreateItemAsync(BoardSection section, string title, Guid? itemId = null,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.CreateItemAsync(userId, section, title, itemId, cancellationToken);
    }

    public async Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.RenameItemAsync(
            userId, section, itemId, title, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<bool> DeleteItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.DeleteItemAsync(
            userId, section, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok;
    }

    public async Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.ArchiveItemAsync(
            userId, section, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardItem?> UnarchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.UnarchiveItemAsync(
            userId, section, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardSnapshot> GetArchivedSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.GetArchivedSnapshotAsync(userId, cancellationToken);
    }

    public async Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.ToggleItemAsync(
            userId, section, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.CompleteDailyForDateAsync(
            userId, itemId, completedOn, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.IncrementHabitPlusAsync(
            userId, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.IncrementHabitMinusAsync(
            userId, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        UpdateHabitArgs args,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.UpdateHabitAsync(userId, itemId, args, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        UpdateTodoArgs args,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.UpdateTodoAsync(userId, itemId, args, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        UpdateDailyArgs args,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.UpdateDailyAsync(userId, itemId, args, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<Dictionary<Guid, int>> GetStreakMapAsync(CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.GetDailyStreakMapAsync(userId, cancellationToken);
    }

    private async Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        AuthenticationState authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        System.Security.Claims.ClaimsPrincipal user = authState.User;
        return await demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
    }
}
