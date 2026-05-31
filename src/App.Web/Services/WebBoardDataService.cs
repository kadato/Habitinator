using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebBoardDataService : IBoardDataService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly BoardPersistenceService _boardPersistenceService;
    private readonly DemoUserResolver _demoUserResolver;

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
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.GetSnapshotAsync(userId, cancellationToken);
    }

    public async Task<BoardItem> CreateItemAsync(BoardSection section, string title, Guid? itemId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.CreateItemAsync(userId, section, title, itemId, cancellationToken);
    }

    public async Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.RenameItemAsync(userId, section, itemId, title, cancellationToken);
    }

    public async Task<bool> DeleteItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.DeleteItemAsync(userId, section, itemId, cancellationToken);
    }

    public async Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        var r = await _boardPersistenceService.ArchiveItemForApiAsync(userId, section, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardItem?> UnarchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        var r = await _boardPersistenceService.UnarchiveItemForApiAsync(userId, section, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardSnapshot> GetArchivedSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.GetArchivedSnapshotAsync(userId, cancellationToken);
    }

    public async Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.ToggleItemAsync(userId, section, itemId, cancellationToken);
    }

    public async Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.CompleteDailyForDateAsync(userId, itemId, completedOn, cancellationToken);
    }

    public async Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.IncrementHabitPlusAsync(userId, itemId, cancellationToken);
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.IncrementHabitMinusAsync(userId, itemId, cancellationToken);
    }

    public async Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        bool trackPlus,
        bool trackMinus,
        HabitResetPeriod resetPeriod,
        int counter,
        int negativeCounter,
        string? checklistJson = null,
        double? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.UpdateHabitAsync(
            userId,
            itemId,
            title,
            notes,
            tags,
            trackPlus,
            trackMinus,
            resetPeriod,
            counter,
            negativeCounter,
            checklistJson,
            sortOrder,
            cancellationToken);
    }

    public async Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        string? checklistJson,
        DateTime? dueDate,
        double? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.UpdateTodoAsync(
            userId,
            itemId,
            title,
            notes,
            tags,
            checklistJson,
            dueDate,
            sortOrder,
            cancellationToken);
    }

    public async Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        string title,
        string? notes,
        string? tags,
        DateTime? startDate,
        DailyRepeatType repeatType,
        int repeatInterval,
        string? checklistJson,
        int streak,
        double? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserIdAsync(cancellationToken);
        return await _boardPersistenceService.UpdateDailyAsync(
            userId,
            itemId,
            title,
            notes,
            tags,
            startDate,
            repeatType,
            repeatInterval,
            checklistJson,
            streak,
            sortOrder,
            cancellationToken);
    }

    private async Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        return await _demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
    }
}
