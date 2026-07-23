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
        return await boardPersistenceService.RenameItemAsync(userId, section, itemId, title, cancellationToken);
    }

    public async Task<bool> DeleteItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.DeleteItemAsync(userId, section, itemId, cancellationToken);
    }

    public async Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.ArchiveItemForApiAsync(userId, section, itemId, null, cancellationToken);
        return r.Status == BoardMutationStatus.Ok ? r.Item : null;
    }

    public async Task<BoardItem?> UnarchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        BoardMutationResult r = await boardPersistenceService.UnarchiveItemForApiAsync(userId, section, itemId, null, cancellationToken);
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
        return await boardPersistenceService.ToggleItemAsync(userId, section, itemId, cancellationToken);
    }

    public async Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.CompleteDailyForDateAsync(userId, itemId, completedOn, cancellationToken);
    }

    public async Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.IncrementHabitPlusAsync(userId, itemId, cancellationToken);
    }

    public async Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.IncrementHabitMinusAsync(userId, itemId, cancellationToken);
    }

    public async Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        App.Shared.RCL.Services.UpdateHabitArgs args,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.UpdateHabitAsync(
            userId,
            itemId,
            new App.Web.Services.UpdateHabitArgs(
                args.Title,
                args.Notes,
                args.Tags,
                args.TrackPlus,
                args.TrackMinus,
                args.ResetPeriod,
                args.Counter,
                args.NegativeCounter,
                args.ChecklistJson,
                args.SortOrder,
                ExpectedUpdatedAtUtc: null),
            cancellationToken);
    }

    public async Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        App.Shared.RCL.Services.UpdateTodoArgs args,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.UpdateTodoAsync(
            userId,
            itemId,
            new App.Web.Services.UpdateTodoArgs(
                args.Title,
                args.Notes,
                args.Tags,
                args.ChecklistJson,
                args.DueDate,
                args.SortOrder,
                ExpectedUpdatedAtUtc: null),
            cancellationToken);
    }

    public async Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        App.Shared.RCL.Services.UpdateDailyArgs args,
        CancellationToken cancellationToken = default)
    {
        Guid userId = await GetCurrentUserIdAsync(cancellationToken);
        return await boardPersistenceService.UpdateDailyAsync(
            userId,
            itemId,
            new App.Web.Services.UpdateDailyArgs(
                args.Title,
                args.Notes,
                args.Tags,
                args.StartDate,
                args.RepeatType,
                args.RepeatInterval,
                args.ChecklistJson,
                args.Streak,
                args.SortOrder,
                ExpectedUpdatedAtUtc: null),
            cancellationToken);
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
