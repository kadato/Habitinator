using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Remote;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services.Board.Local;

public sealed partial class LocalFirstBoardDataService
{
    public async Task<bool> TryDrainOneOutboxOperationAsync(CancellationToken cancellationToken = default)
    {
        Guid operationId;

        await store.EnsureReadyAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userKey = await ResolveAuthedUserKeyAsync(cancellationToken);
            if (userKey is null)
            {
                return false;
            }

            await EnsureUserScopeAsync(userKey, cancellationToken);

            var head = (await store.ListOutboxAsync(userKey, cancellationToken))
                .OrderBy(o => o.CreatedAtUtc)
                .ThenBy(o => o.OperationId)
                .FirstOrDefault();

            if (head is null)
            {
                return false;
            }

            if (head.AttemptCount > 0 && head.LastAttemptUtc is { } last)
            {
                var wait = Backoff(head.AttemptCount);
                if (DateTime.UtcNow < last + wait)
                {
                    return false;
                }
            }

            operationId = head.OperationId;
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await ExecuteOutboxRemoteByIdAsync(operationId, remote, cancellationToken);
            await DropOutboxOperationAsync(operationId, cancellationToken);
            return true;
        }
        catch (BoardRemoteConflictException ex)
        {
            return await HandleRemoteConflictAsync(operationId, ex, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outbox operation {OperationId} failed.", operationId);
            await RecordOutboxFailureAsync(operationId, ex.Message, cancellationToken);
            return false;
        }
    }

    private static bool AreItemsContentEqual(BoardItem a, BoardItem b)
    {
        return string.Equals(a.Title, b.Title, StringComparison.Ordinal) &&
               a.IsCompleted == b.IsCompleted &&
               a.Counter == b.Counter &&
               string.Equals(a.Notes ?? string.Empty, b.Notes ?? string.Empty, StringComparison.Ordinal) &&
               string.Equals(a.Tags ?? string.Empty, b.Tags ?? string.Empty, StringComparison.Ordinal) &&
               a.TrackPlus == b.TrackPlus &&
               a.TrackMinus == b.TrackMinus &&
               a.NegativeCounter == b.NegativeCounter &&
               a.ResetPeriod == b.ResetPeriod &&
               a.HabitPeriodStart == b.HabitPeriodStart &&
               a.DailyStartDate == b.DailyStartDate &&
               a.DailyRepeat == b.DailyRepeat &&
               a.DailyRepeatInterval == b.DailyRepeatInterval &&
               a.DailyWeekdays == b.DailyWeekdays &&
               string.Equals(a.ChecklistJson ?? string.Empty, b.ChecklistJson ?? string.Empty, StringComparison.Ordinal) &&
               a.DailyLastCompletedOn == b.DailyLastCompletedOn &&
               a.TodoDueDate == b.TodoDueDate &&
               NullableDoubleEquals(a.SortOrder, b.SortOrder) &&
               a.IsArchived == b.IsArchived;
    }

    private static bool NullableDoubleEquals(double? a, double? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return Math.Abs(a.Value - b.Value) < 0.0001;
    }

    private async Task<bool> HandleRemoteConflictAsync(
        Guid operationId,
        BoardRemoteConflictException ex,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(ex, "Outbox operation {OperationId} returned 409 Conflict.", operationId);

        var serverItem = ex.ServerItem;
        if (serverItem is null)
        {
            logger.LogWarning("Conflict exception has no server item; dropping op.");
            await DropOutboxOperationAsync(operationId, cancellationToken);
            RequestSyncSoon();
            return false;
        }

        BoardItem? localItem = null;
        var section = BoardSection.Todo;
        var localTime = DateTimeOffset.MinValue;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var row = await FindItemAnyUserAsync(serverItem.Id, cancellationToken);
            if (row is not null)
            {
                localItem = row.ToModel();
                section = row.Section;
            }

            var opRow = await store.FindOutboxAsync(operationId, cancellationToken);
            if (opRow is not null)
            {
                localTime = new DateTimeOffset(opRow.CreatedAtUtc, TimeSpan.Zero);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (localItem is null)
        {
            logger.LogWarning("Local item not found for conflict resolution; dropping op.");
            await DropOutboxOperationAsync(operationId, cancellationToken);
            RequestSyncSoon();
            return false;
        }

        if (AreItemsContentEqual(localItem, serverItem))
        {
            logger.LogInformation("Conflict detected but items are content-identical. Auto-resolving by keeping Server version silently.");
            await ResolveConflictKeepServerAsync(operationId, serverItem, section, cancellationToken);
            return false;
        }

        var serverTime = serverItem.ServerUpdatedAtUtc ?? DateTimeOffset.MinValue;
        var localTimeInServerClock = localTime;
        if (ex.ServerTimeUtc is { } serverNow)
        {
            // The device clock may be skewed. Express the local edit in the server's clock using
            // the offset implied by the 409 response's Date header, so the comparison is between
            // two readings of the same clock, not two unsynchronized ones.
            localTimeInServerClock = localTime + (serverNow - DateTimeOffset.UtcNow);
        }

        if (localTimeInServerClock >= serverTime)
        {
            logger.LogInformation(
                "Conflict auto-resolved via Last-Write-Wins: Keeping Device version (Local: {LocalTime} as {LocalInServerClock} >= Server: {ServerTime}).",
                localTime,
                localTimeInServerClock,
                serverTime);
            await ResolveConflictKeepMineAsync(operationId, serverItem, cancellationToken);
        }
        else
        {
            logger.LogInformation(
                "Conflict auto-resolved via Last-Write-Wins: Keeping Server version (Local: {LocalTime} as {LocalInServerClock} < Server: {ServerTime}).",
                localTime,
                localTimeInServerClock,
                serverTime);
            await ResolveConflictKeepServerAsync(operationId, serverItem, section, cancellationToken);
        }

        return false;
    }

    private async Task<BoardLocalRow?> FindItemAnyUserAsync(Guid id, CancellationToken cancellationToken)
    {
        var userKey = await users.GetUserKeyAsync(cancellationToken);
        if (!string.IsNullOrEmpty(userKey))
        {
            var scoped = await store.FindItemAsync(userKey, id, cancellationToken);
            if (scoped is not null)
            {
                return scoped;
            }
        }

        var meta = await store.GetMetaAsync(cancellationToken);
        if (!string.IsNullOrEmpty(meta.BoundUserKey) && meta.BoundUserKey != userKey)
        {
            return await store.FindItemAsync(meta.BoundUserKey, id, cancellationToken);
        }

        return null;
    }

    private async Task ResolveConflictKeepMineAsync(Guid operationId, BoardItem serverItem, CancellationToken cancellationToken)
    {
        logger.LogInformation("Conflict auto-resolved: keeping the device version.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var opRow = await store.FindOutboxAsync(operationId, cancellationToken);
            if (opRow is not null)
            {
                opRow.PayloadJson = BoardOutboxPayloadMapper.RemapExpectedVersion(
                    opRow.Kind,
                    opRow.PayloadJson,
                    serverItem.ServerUpdatedAtUtc ?? DateTimeOffset.UtcNow);
                opRow.AttemptCount = 0;
                opRow.LastError = null;
                await store.UpdateOutboxAsync(opRow, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ResolveConflictKeepServerAsync(
        Guid operationId,
        BoardItem serverItem,
        BoardSection section,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Conflict auto-resolved: keeping the server version.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await store.DeleteOutboxAsync(operationId, cancellationToken);

            var localRow = await FindItemAnyUserAsync(serverItem.Id, cancellationToken);
            if (localRow is not null)
            {
                var userKey = localRow.UserKey;
                var updated = BoardLocalRow.FromModel(section, userKey, serverItem, false);
                updated.UserKey = userKey;
                await store.UpsertItemAsync(updated, cancellationToken);

                // Newer pending operations for the same item were built against the version this
                // one conflicted with. Rebase them onto the server version so they apply on top
                // instead of conflicting one after another.
                if (serverItem.ServerUpdatedAtUtc is { } serverVersion)
                {
                    await RebasePendingOperationsAsync(userKey, serverItem.Id, serverVersion, cancellationToken);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        RequestSyncSoon();
    }

    private async Task RebasePendingOperationsAsync(
        string userKey,
        Guid itemId,
        DateTimeOffset serverVersion,
        CancellationToken cancellationToken)
    {
        foreach (var row in await store.ListOutboxAsync(userKey, cancellationToken))
        {
            if (!BoardOutboxReferencedIds.ReferencesItem(row.Kind, row.PayloadJson, itemId))
            {
                continue;
            }

            var remapped = BoardOutboxPayloadMapper.RemapExpectedVersion(row.Kind, row.PayloadJson, serverVersion);
            if (!string.Equals(remapped, row.PayloadJson, StringComparison.Ordinal))
            {
                row.PayloadJson = remapped;
                row.AttemptCount = 0;
                row.LastError = null;
                await store.UpdateOutboxAsync(row, cancellationToken);
            }
        }
    }

    public async Task<string?> TryGetStuckOutboxHintAsync(int minAttempts, CancellationToken cancellationToken = default)
    {
        await store.EnsureReadyAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userKey = await ResolveAuthedUserKeyAsync(cancellationToken);
            if (userKey is null)
            {
                return null;
            }

            var row = (await store.ListOutboxAsync(userKey, cancellationToken))
                .Where(o => o.AttemptCount >= minAttempts)
                .OrderByDescending(o => o.AttemptCount)
                .FirstOrDefault();

            if (row is null)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(row.LastError)
                ? "Some changes could not sync. Try again when online."
                : $"Sync issue: {row.LastError}";
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Queued writes the server has not acknowledged yet. Zero when signed out.</summary>
    public async Task<int> GetPendingOutboxCountAsync(CancellationToken cancellationToken = default)
    {
        await store.EnsureReadyAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userKey = await ResolveAuthedUserKeyAsync(cancellationToken);
            if (userKey is null)
            {
                return 0;
            }

            return (await store.ListOutboxAsync(userKey, cancellationToken)).Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteOutboxRemoteByIdAsync(Guid operationId, RemoteBoardDataService api,
        CancellationToken cancellationToken)
    {
        BoardOutboxEntry head;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            head = await store.FindOutboxAsync(operationId, cancellationToken)
                   ?? throw new InvalidOperationException("Outbox entry disappeared.");
            _inFlightOutboxOperations.Add(operationId);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await ExecuteOutboxRemoteAsync(head, api, cancellationToken);
        }
        finally
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                _inFlightOutboxOperations.Remove(operationId);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private static T DeserializePayload<T>(BoardOutboxEntry head, string failureMessage)
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(head.PayloadJson, BoardOutboxJson.Options)
               ?? throw new InvalidOperationException(failureMessage);
    }

    private async Task ExecuteOutboxRemoteAsync(BoardOutboxEntry head, RemoteBoardDataService api, CancellationToken cancellationToken)
    {
        async Task Patch(Guid itemId, BoardItem? updated) =>
            await PatchLocalAsync(itemId, head.UserKey, updated, cancellationToken);

        switch (head.Kind)
        {
            case BoardOutboxOperationKind.Create:
                {
                    var p = DeserializePayload<CreateOutboxPayload>(head, "Invalid create payload.");
                    var serverItem = await api.CreateItemAsync(p.Section, p.Title, p.ClientItemId, head.OperationId, cancellationToken);
                    await CommitCreateSuccessAsync(head.OperationId, p.ClientItemId, p.Section, serverItem, head.UserKey, cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.Rename:
                {
                    var p = DeserializePayload<RenameOutboxPayload>(head, "Invalid rename payload.");
                    var updated = await api.RenameItemAsync(
                        p.Section,
                        p.ItemId,
                        p.Title,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await Patch(p.ItemId, updated);
                    return;
                }
            case BoardOutboxOperationKind.Delete:
                {
                    var p = DeserializePayload<SectionItemOutboxPayload>(head, "Invalid delete payload.");
                    _ = await api.DeleteItemAsync(
                        p.Section,
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    return;
                }
            case BoardOutboxOperationKind.Toggle:
                {
                    var p = DeserializePayload<SectionItemOutboxPayload>(head, "Invalid toggle payload.");
                    var updated = await api.ToggleItemAsync(
                        p.Section,
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    updated ??= await api.GetItemAsync(p.ItemId, cancellationToken);
                    await Patch(p.ItemId, updated);
                    return;
                }
            case BoardOutboxOperationKind.CompleteDailyForDate:
                {
                    var p = DeserializePayload<CompleteDailyOutboxPayload>(head, "Invalid complete-daily payload.");
                    var updated = await api.CompleteDailyForDateAsync(
                        p.ItemId,
                        p.CompletedOn,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    updated ??= await api.GetItemAsync(p.ItemId, cancellationToken);
                    await Patch(p.ItemId, updated);
                    return;
                }
            case BoardOutboxOperationKind.HabitIncrement:
                {
                    var p = DeserializePayload<ItemIdOutboxPayload>(head, "Invalid habit+ payload.");
                    var updated = await api.IncrementHabitPlusAsync(
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await Patch(p.ItemId, updated);
                    return;
                }
            case BoardOutboxOperationKind.HabitDecrement:
                {
                    var p = DeserializePayload<ItemIdOutboxPayload>(head, "Invalid habit- payload.");
                    var updated = await api.IncrementHabitMinusAsync(
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await Patch(p.ItemId, updated);
                    return;
                }
            case BoardOutboxOperationKind.UpdateHabit:
                {
                    var p = DeserializePayload<UpdateHabitOutboxPayload>(head, "Invalid habit update payload.");
                    var updated = await api.UpdateHabitAsync(
                        p.ItemId,
                        new UpdateHabitArgs(
                            p.Title,
                            p.Notes,
                            p.Tags,
                            p.TrackPlus,
                            p.TrackMinus,
                            p.ResetPeriod,
                            p.Counter,
                            p.NegativeCounter,
                            p.ChecklistJson,
                            p.SortOrder),
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await Patch(p.ItemId, updated);
                    return;
                }
            case BoardOutboxOperationKind.UpdateTodo:
                {
                    var p = DeserializePayload<UpdateTodoOutboxPayload>(head, "Invalid todo update payload.");
                    var updated = await api.UpdateTodoAsync(
                        p.ItemId,
                        new UpdateTodoArgs(
                            p.Title,
                            p.Notes,
                            p.Tags,
                            p.ChecklistJson,
                            p.DueDate,
                            p.SortOrder),
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await Patch(p.ItemId, updated);
                    return;
                }
            case BoardOutboxOperationKind.UpdateDaily:
                {
                    var p = DeserializePayload<UpdateDailyOutboxPayload>(head, "Invalid daily update payload.");
                    var updated = await api.UpdateDailyAsync(
                        p.ItemId,
                        new UpdateDailyArgs(
                            p.Title,
                            p.Notes,
                            p.Tags,
                            p.StartDate,
                            p.Repeat,
                            p.RepeatInterval,
                            p.ChecklistJson,
                            p.Counter,
                            p.SortOrder,
                            null,
                            p.Weekdays),
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await Patch(p.ItemId, updated);
                    return;
                }
            case BoardOutboxOperationKind.Archive:
                {
                    var p = DeserializePayload<SectionItemOutboxPayload>(head, "Invalid archive payload.");
                    var updated = await api.ArchiveItemAsync(
                        p.Section,
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await Patch(p.ItemId, updated);
                    return;
                }
            case BoardOutboxOperationKind.Unarchive:
                {
                    var p = DeserializePayload<SectionItemOutboxPayload>(head, "Invalid unarchive payload.");
                    var updated = await api.UnarchiveItemAsync(
                        p.Section,
                        p.ItemId,
                        head.OperationId,
                        p.ExpectedServerUpdatedAtUtc,
                        cancellationToken);
                    await Patch(p.ItemId, updated);
                    return;
                }
            default:
                throw new InvalidOperationException($"Unknown outbox kind {head.Kind}.");
        }
    }

    private async Task DropOutboxOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await store.DeleteOutboxAsync(operationId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RecordOutboxFailureAsync(Guid operationId, string message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var still = await store.FindOutboxAsync(operationId, cancellationToken);
            if (still is not null)
            {
                still.AttemptCount++;
                still.LastAttemptUtc = DateTime.UtcNow;
                still.LastError = message;
                await store.UpdateOutboxAsync(still, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CommitCreateSuccessAsync(
        Guid operationId, Guid clientId, BoardSection section, BoardItem serverItem, string userKey, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var localRow = await store.FindItemAsync(userKey, clientId, cancellationToken);
            if (localRow is null)
            {
                // The item was deleted while this create was in flight. The delete is queued, and
                // its payload is remapped below, so keep the item deleted locally.
                logger.LogInformation(
                    "Create operation {OperationId} was acknowledged after its local item was removed. Keeping it deleted.",
                    operationId);
            }
            else
            {
                await store.DeleteItemAsync(userKey, clientId, cancellationToken);
                await store.UpsertItemAsync(BoardLocalRow.FromModel(section, userKey, serverItem, false), cancellationToken);
            }

            foreach (var row in await store.ListOutboxAsync(userKey, cancellationToken))
            {
                var remapped = BoardOutboxPayloadMapper.RemapClientToServerId(row.Kind, row.PayloadJson, clientId, serverItem.Id);
                if (!string.Equals(remapped, row.PayloadJson, StringComparison.Ordinal))
                {
                    row.PayloadJson = remapped;
                    await store.UpdateOutboxAsync(row, cancellationToken);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PatchLocalAsync(Guid itemId, string userKey, BoardItem? serverItem, CancellationToken cancellationToken)
    {
        if (serverItem is null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var row = await store.FindItemAsync(userKey, itemId, cancellationToken);
            if (row is null)
            {
                return;
            }

            var section = row.Section;
            var awaiting = row.AwaitingServerCreate;
            var updated = BoardLocalRow.FromModel(section, userKey, serverItem, awaiting);
            updated.UserKey = row.UserKey;
            await store.UpsertItemAsync(updated, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ApplyLocalToggle(BoardSection section, BoardLocalRow row, DateOnly today)
    {
        switch (section)
        {
            case BoardSection.Habit:
                row.IsCompleted = !row.IsCompleted;
                break;
            case BoardSection.Daily:
                {
                    (row.DailyLastCompletedOn, row.IsCompleted) = DailySchedule.ToggleForToday(
                        row.DailyLastCompletedOn, row.IsCompleted, today);
                    break;
                }
            case BoardSection.Todo:
                row.IsCompleted = !row.IsCompleted;
                break;
        }
    }

    private async Task<T> MutateAsync<T>(Func<IBoardLocalStore, string, Task<T>> action, CancellationToken cancellationToken)
    {
        await store.EnsureReadyAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var userKey = await ResolveAuthedUserKeyAsync(cancellationToken)
                ?? throw new InvalidOperationException("Sign in to change your board.");

            await EnsureUserScopeAsync(userKey, cancellationToken);
            var result = await action(store, userKey);
            _cachedSnapshot = null;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> MutateWithSyncAsync<T>(Func<IBoardLocalStore, string, Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            var result = await MutateAsync(action, cancellationToken);
            if (result is BoardItem bi)
            {
                InvalidateStatsCache(bi);
            }
            else if (result is not null)
            {
                InvalidateStatsCache();
            }

            return result;
        }
        finally
        {
            RequestSyncSoon();
        }
    }

    private void InvalidateStatsCache(BoardItem? item = null)
    {
        try
        {
            var stats = services.GetService<IActivityStatisticsReader>();
            if (stats != null)
            {
                if (item != null)
                {
                    stats.InvalidateForItem(item);
                }
                else
                {
                    stats.InvalidateCache();
                }
            }

            var offline = services.GetService<OfflineActivityStatisticsProvider>();
            if (offline != null)
            {
                if (item != null)
                {
                    offline.InvalidateForTags(BoardTagUtil.ParseTags(item.Tags));
                }
                else
                {
                    offline.InvalidateForTags(null);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogTrace(ex, "Could not invalidate stats cache.");
        }
    }

    private void RequestSyncSoon()
    {
        try
        {
            services.GetService<IBoardSyncRequestor>()?.RequestSync();
        }
        catch (Exception ex)
        {
            logger.LogTrace(ex, "Could not request board sync.");
        }
    }

    private static async Task EnqueueStaticAsync<T>(IBoardLocalStore local, string userKey, BoardOutboxOperationKind kind, T payload, CancellationToken cancellationToken)
    {
        await local.EnqueueOutboxAsync(
            new BoardOutboxEntry
            {
                OperationId = Guid.NewGuid(),
                UserKey = userKey,
                Kind = kind,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload, BoardOutboxJson.Options),
                CreatedAtUtc = DateTime.UtcNow
            },
            cancellationToken);
    }

    private async Task<bool> TryCoalesceDeletePendingCreateAsync(IBoardLocalStore local, string userKey, Guid itemId,
        CancellationToken cancellationToken)
    {
        var pending = await local.ListOutboxAsync(userKey, cancellationToken);

        foreach (var row in pending.Where(o => o.Kind == BoardOutboxOperationKind.Create))
        {
            var p = System.Text.Json.JsonSerializer.Deserialize<CreateOutboxPayload>(row.PayloadJson, BoardOutboxJson.Options);
            if (p?.ClientItemId != itemId)
            {
                continue;
            }

            // The create may already be on the wire, or an earlier attempt may have reached the
            // server before its response was lost. In both cases the delete has to travel as a
            // real operation: the create is retried, acknowledged, and remapped to the server id,
            // and the queued delete then removes the server row.
            if (_inFlightOutboxOperations.Contains(row.OperationId) || row.AttemptCount > 0)
            {
                return false;
            }

            // The server has never seen this item. Remove every pending operation for it, not
            // just the create, so no orphan operation is later sent with an unknown id.
            foreach (var op in pending.Where(o => o.OperationId != row.OperationId
                && !_inFlightOutboxOperations.Contains(o.OperationId)
                && BoardOutboxReferencedIds.ReferencesItem(o.Kind, o.PayloadJson, itemId)))
            {
                await local.DeleteOutboxAsync(op.OperationId, cancellationToken);
            }

            await local.DeleteOutboxAsync(row.OperationId, cancellationToken);
            await local.DeleteItemAsync(userKey, itemId, cancellationToken);
            return true;
        }

        return false;
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(attempt, 8))));
}
