using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Auth;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebBoardDataService(
    AuthenticationStateProvider authenticationStateProvider,
    BoardPersistenceService boardPersistenceService) : IBoardDataService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<BoardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userId = await GetCurrentUserIdAsync(cancellationToken);
            return await boardPersistenceService.GetSnapshotAsync(userId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BoardItem?> GetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userId = await GetCurrentUserIdAsync(cancellationToken);
            return await boardPersistenceService.GetItemAsync(userId, itemId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BoardItem> CreateItemAsync(BoardSection section, string title, Guid? itemId = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userId = await GetCurrentUserIdAsync(cancellationToken);
            return await boardPersistenceService.CreateItemAsync(userId, section, title, itemId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<BoardItem?> RenameItemAsync(BoardSection section, Guid itemId, string title,
        CancellationToken cancellationToken = default) =>
        MutateAsync((userId, ct) => boardPersistenceService.RenameItemAsync(userId, section, itemId, title, null, ct),
            cancellationToken);

    public Task<bool> DeleteItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateStatusAsync((userId, ct) => boardPersistenceService.DeleteItemAsync(userId, section, itemId, null, ct),
            cancellationToken);

    public Task<BoardItem?> ArchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateAsync((userId, ct) => boardPersistenceService.ArchiveItemAsync(userId, section, itemId, null, ct),
            cancellationToken);

    public Task<BoardItem?> UnarchiveItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateAsync((userId, ct) => boardPersistenceService.UnarchiveItemAsync(userId, section, itemId, null, ct),
            cancellationToken);

    public async Task<BoardSnapshot> GetArchivedSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userId = await GetCurrentUserIdAsync(cancellationToken);
            return await boardPersistenceService.GetArchivedSnapshotAsync(userId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<BoardItem?> ToggleItemAsync(BoardSection section, Guid itemId,
        CancellationToken cancellationToken = default) =>
        MutateAsync((userId, ct) => boardPersistenceService.ToggleItemAsync(userId, section, itemId, null, ct),
            cancellationToken);

    public Task<BoardItem?> CompleteDailyForDateAsync(Guid itemId, DateOnly completedOn,
        CancellationToken cancellationToken = default) =>
        MutateAsync((userId, ct) => boardPersistenceService.CompleteDailyForDateAsync(userId, itemId, completedOn, null, ct),
            cancellationToken);

    public Task<BoardItem?> IncrementHabitPlusAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        MutateAsync((userId, ct) => boardPersistenceService.IncrementHabitPlusAsync(userId, itemId, null, ct),
            cancellationToken);

    public Task<BoardItem?> IncrementHabitMinusAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        MutateAsync((userId, ct) => boardPersistenceService.IncrementHabitMinusAsync(userId, itemId, null, ct),
            cancellationToken);

    public Task<BoardItem?> UpdateHabitAsync(
        Guid itemId,
        UpdateHabitArgs args,
        CancellationToken cancellationToken = default) =>
        MutateAsync((userId, ct) => boardPersistenceService.UpdateHabitAsync(userId, itemId, args, ct),
            cancellationToken);

    public Task<BoardItem?> UpdateTodoAsync(
        Guid itemId,
        UpdateTodoArgs args,
        CancellationToken cancellationToken = default) =>
        MutateAsync((userId, ct) => boardPersistenceService.UpdateTodoAsync(userId, itemId, args, ct),
            cancellationToken);

    public Task<BoardItem?> UpdateDailyAsync(
        Guid itemId,
        UpdateDailyArgs args,
        CancellationToken cancellationToken = default) =>
        MutateAsync((userId, ct) => boardPersistenceService.UpdateDailyAsync(userId, itemId, args, ct),
            cancellationToken);

    public async Task<Dictionary<Guid, int>> GetStreakMapAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userId = await GetCurrentUserIdAsync(cancellationToken);
            return await boardPersistenceService.GetDailyStreakMapAsync(userId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        return AuthenticatedUserId.TryGet(state.User)
            ?? throw new InvalidOperationException("Sign in required.");
    }
    private async Task<BoardItem?> MutateAsync(Func<Guid, CancellationToken, Task<BoardMutationResult>> op,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userId = await GetCurrentUserIdAsync(cancellationToken);
            var r = await op(userId, cancellationToken);
            return r.Status == BoardMutationStatus.Ok ? r.Item : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> MutateStatusAsync(Func<Guid, CancellationToken, Task<BoardMutationResult>> op,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userId = await GetCurrentUserIdAsync(cancellationToken);
            var r = await op(userId, cancellationToken);
            return r.Status == BoardMutationStatus.Ok;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
