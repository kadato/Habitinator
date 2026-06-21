using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public enum ConflictResolutionChoice
{
    KeepMine,
    KeepServer,
    Cancel
}

public sealed record ConflictInfo(
    Guid OperationId,
    BoardItem LocalItem,
    BoardItem ServerItem,
    BoardSection Section);

public sealed class ConflictResolutionService
{
    private readonly Dictionary<Guid, TaskCompletionSource<ConflictResolutionChoice>> _pendingConflicts = new();
    private readonly object _lock = new();

    public event Action<ConflictInfo>? ConflictDetected;

    public async Task<ConflictResolutionChoice> WaitForResolutionAsync(
        ConflictInfo conflict,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<ConflictResolutionChoice> tcs;
        lock (_lock)
        {
            if (_pendingConflicts.TryGetValue(conflict.OperationId, out var existing))
            {
                tcs = existing;
            }
            else
            {
                tcs = new TaskCompletionSource<ConflictResolutionChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingConflicts[conflict.OperationId] = tcs;

                // Notify UI listeners (subscribers like MainBoard)
                ConflictDetected?.Invoke(conflict);
            }
        }

        // Handle cancellation
        await using (cancellationToken.Register(() => tcs.TrySetResult(ConflictResolutionChoice.Cancel)))
        {
            return await tcs.Task;
        }
    }

    public void Resolve(Guid operationId, ConflictResolutionChoice choice)
    {
        lock (_lock)
        {
            if (_pendingConflicts.Remove(operationId, out var tcs))
            {
                tcs.TrySetResult(choice);
            }
        }
    }
}
