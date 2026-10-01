namespace App.Shared.RCL.Services.Board.Local;

public sealed class InMemoryBoardLocalStore : IBoardLocalStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, BoardLocalRow> _items = [];
    private readonly Dictionary<Guid, BoardOutboxEntry> _outbox = [];
    private BoardStoreMeta _meta = new();

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<BoardStoreMeta> GetMetaAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(new BoardStoreMeta
            {
                BoundUserKey = _meta.BoundUserKey,
                LastSyncCursorUtc = _meta.LastSyncCursorUtc
            });
        }
    }

    public Task SetMetaAsync(BoardStoreMeta meta, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _meta = new BoardStoreMeta
            {
                BoundUserKey = meta.BoundUserKey,
                LastSyncCursorUtc = meta.LastSyncCursorUtc
            };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BoardLocalRow>> ListItemsAsync(string userKey, bool includeArchived, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlyList<BoardLocalRow> rows = _items.Values
                .Where(x => x.UserKey == userKey && (includeArchived || !x.IsArchived))
                .Select(CloneRow)
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<BoardLocalRow?> FindItemAsync(string userKey, Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_items.TryGetValue(id, out var row) && row.UserKey == userKey)
            {
                return Task.FromResult<BoardLocalRow?>(CloneRow(row));
            }

            return Task.FromResult<BoardLocalRow?>(null);
        }
    }

    public Task UpsertItemAsync(BoardLocalRow row, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _items[row.Id] = CloneRow(row);
        }

        return Task.CompletedTask;
    }

    public Task DeleteItemAsync(string userKey, Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_items.TryGetValue(id, out var row) && row.UserKey == userKey)
            {
                _items.Remove(id);
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteAllItemsAsync(string userKey, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var id in _items.Where(kv => kv.Value.UserKey == userKey).Select(kv => kv.Key).ToList())
            {
                _items.Remove(id);
            }
        }

        return Task.CompletedTask;
    }

    public Task ReplaceAllItemsAsync(string userKey, IReadOnlyList<BoardLocalRow> rows, string? cursor, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var id in _items.Where(kv => kv.Value.UserKey == userKey).Select(kv => kv.Key).ToList())
            {
                _items.Remove(id);
            }

            foreach (var row in rows)
            {
                _items[row.Id] = CloneRow(row);
            }

            _meta.LastSyncCursorUtc = cursor;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BoardOutboxEntry>> ListOutboxAsync(string userKey, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlyList<BoardOutboxEntry> rows = _outbox.Values
                .Where(x => x.UserKey == userKey)
                .Select(CloneOutbox)
                .ToList();
            return Task.FromResult(rows);
        }
    }

    public Task<BoardOutboxEntry?> FindOutboxAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_outbox.TryGetValue(operationId, out var row))
            {
                return Task.FromResult<BoardOutboxEntry?>(CloneOutbox(row));
            }

            return Task.FromResult<BoardOutboxEntry?>(null);
        }
    }

    public Task EnqueueOutboxAsync(BoardOutboxEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _outbox[entry.OperationId] = CloneOutbox(entry);
        }

        return Task.CompletedTask;
    }

#pragma warning disable S4144 // Enqueue and Update share the same upsert shape on purpose.
    public Task UpdateOutboxAsync(BoardOutboxEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _outbox[entry.OperationId] = CloneOutbox(entry);
        }

        return Task.CompletedTask;
    }
#pragma warning restore S4144

    public Task DeleteOutboxAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _outbox.Remove(operationId);
        }

        return Task.CompletedTask;
    }

    public Task ClearOutboxForUserAsync(string userKey, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var id in _outbox.Where(kv => kv.Value.UserKey == userKey).Select(kv => kv.Key).ToList())
            {
                _outbox.Remove(id);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> HasOutboxAsync(string userKey, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_outbox.Values.Any(x => x.UserKey == userKey));
        }
    }

    public Task ClearAllStateAsync(CancellationToken cancellationToken = default)
    {
        Reset();
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Reset()
    {
        lock (_lock)
        {
            _items.Clear();
            _outbox.Clear();
            _meta = new BoardStoreMeta();
        }
    }

    public BoardLocalState ExportState()
    {
        lock (_lock)
        {
            return new BoardLocalState(
                _items.Values.Select(CloneRow).ToList(),
                _outbox.Values.Select(CloneOutbox).ToList(),
                new BoardStoreMeta
                {
                    BoundUserKey = _meta.BoundUserKey,
                    LastSyncCursorUtc = _meta.LastSyncCursorUtc
                });
        }
    }

    public void ImportState(BoardLocalState state)
    {
        lock (_lock)
        {
            _items.Clear();
            foreach (var row in state.Items)
            {
                _items[row.Id] = CloneRow(row);
            }

            _outbox.Clear();
            foreach (var entry in state.Outbox)
            {
                _outbox[entry.OperationId] = CloneOutbox(entry);
            }

            _meta = new BoardStoreMeta
            {
                BoundUserKey = state.Meta?.BoundUserKey,
                LastSyncCursorUtc = state.Meta?.LastSyncCursorUtc
            };
        }
    }

    private static BoardLocalRow CloneRow(BoardLocalRow source) => new()
    {
        Id = source.Id,
        UserKey = source.UserKey,
        Section = source.Section,
        Title = source.Title,
        IsCompleted = source.IsCompleted,
        Counter = source.Counter,
        Notes = source.Notes,
        Tags = source.Tags,
        TrackPlus = source.TrackPlus,
        TrackMinus = source.TrackMinus,
        NegativeCounter = source.NegativeCounter,
        ResetPeriod = source.ResetPeriod,
        HabitPeriodStart = source.HabitPeriodStart,
        DailyStartDate = source.DailyStartDate,
        DailyRepeat = source.DailyRepeat,
        DailyRepeatInterval = source.DailyRepeatInterval,
        ChecklistJson = source.ChecklistJson,
        DailyLastCompletedOn = source.DailyLastCompletedOn,
        TodoDueDate = source.TodoDueDate,
        IsArchived = source.IsArchived,
        AwaitingServerCreate = source.AwaitingServerCreate,
        ServerUpdatedAtUtc = source.ServerUpdatedAtUtc,
        CreatedAtUtc = source.CreatedAtUtc,
        SortOrder = source.SortOrder
    };

    private static BoardOutboxEntry CloneOutbox(BoardOutboxEntry source) => new()
    {
        OperationId = source.OperationId,
        UserKey = source.UserKey,
        Kind = source.Kind,
        PayloadJson = source.PayloadJson,
        CreatedAtUtc = source.CreatedAtUtc,
        AttemptCount = source.AttemptCount,
        LastAttemptUtc = source.LastAttemptUtc,
        LastError = source.LastError
    };
}
