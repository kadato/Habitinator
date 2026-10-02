using App.MAUI.Data;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Board.Local;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.MAUI.Services.LocalBoard;

public sealed class SqliteBoardLocalStore(
    IDbContextFactory<LocalBoardDbContext> dbFactory,
    ILogger<SqliteBoardLocalStore> logger) : IBoardLocalStore
{
    private static volatile bool _schemaReady;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (_schemaReady)
        {
            return;
        }

        await SchemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            try
            {
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogTrace(ex, "Failed to enable WAL mode.");
            }

            await EnsureSqliteBoardColumnsAsync(db, cancellationToken);
            MarkSchemaReady();
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private static void MarkSchemaReady()
    {
        _schemaReady = true;
    }

    public async Task<BoardStoreMeta> GetMetaAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var meta = await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken);
        return new BoardStoreMeta
        {
            BoundUserKey = meta?.BoundUserKey,
            LastSyncCursorUtc = meta?.LastSyncCursorUtc
        };
    }

    public async Task SetMetaAsync(BoardStoreMeta meta, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken);
        if (row is null)
        {
            db.Meta.Add(new LocalBoardStoreMetaRow
            {
                Id = 1,
                BoundUserKey = meta.BoundUserKey,
                LastSyncCursorUtc = meta.LastSyncCursorUtc
            });
        }
        else
        {
            row.BoundUserKey = meta.BoundUserKey;
            row.LastSyncCursorUtc = meta.LastSyncCursorUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BoardLocalRow>> ListItemsAsync(string userKey, bool includeArchived, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.BoardItems.AsNoTracking()
            .Where(x => x.UserKey == userKey && (includeArchived || !x.IsArchived))
            .ToListAsync(cancellationToken);
        return rows.Select(ToLocal).ToList();
    }

    public async Task<BoardLocalRow?> FindItemAsync(string userKey, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.BoardItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserKey == userKey && x.Id == id, cancellationToken);
        return row is null ? null : ToLocal(row);
    }

    public async Task UpsertItemAsync(BoardLocalRow row, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.BoardItems.FirstOrDefaultAsync(x => x.Id == row.Id, cancellationToken);
        if (existing is null)
        {
            db.BoardItems.Add(ToRow(row));
        }
        else
        {
            existing.UserKey = row.UserKey;
            existing.Section = row.Section;
            existing.CopyFrom(ToRow(row));
            existing.AwaitingServerCreate = row.AwaitingServerCreate;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteItemAsync(string userKey, Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.BoardItems.Where(x => x.UserKey == userKey && x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteAllItemsAsync(string userKey, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.BoardItems.Where(x => x.UserKey == userKey).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task ReplaceAllItemsAsync(string userKey, IReadOnlyList<BoardLocalRow> rows, string? cursor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.BoardItems.Where(x => x.UserKey == userKey).ExecuteDeleteAsync(cancellationToken);
            foreach (var item in rows)
            {
                db.BoardItems.Add(ToRow(item));
            }

            await db.SaveChangesAsync(cancellationToken);

            var meta = await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken);
            if (meta is not null)
            {
                meta.LastSyncCursorUtc = cursor;
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ApplyDeltaAsync(
        string userKey,
        IReadOnlyList<(BoardSection Section, BoardItem Item)> upserts,
        IReadOnlyList<Guid> deletedIds,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (deletedIds.Count > 0)
            {
                await db.BoardItems
                    .Where(x => x.UserKey == userKey && deletedIds.Contains(x.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            if (upserts.Count > 0)
            {
                var ids = upserts.Select(u => u.Item.Id).ToList();
                var existingById = await db.BoardItems
                    .Where(x => ids.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, cancellationToken);

                foreach (var (section, item) in upserts)
                {
                    var local = BoardLocalRow.FromModel(section, userKey, item, awaitingCreate: false);
                    if (existingById.TryGetValue(item.Id, out var existing))
                    {
                        existing.UserKey = userKey;
                        existing.Section = section;
                        existing.CopyFrom(ToRow(local));
                    }
                    else
                    {
                        db.BoardItems.Add(ToRow(local));
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<BoardOutboxEntry>> ListOutboxAsync(string userKey, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Outbox.AsNoTracking()
            .Where(o => o.UserKey == userKey)
            .ToListAsync(cancellationToken);
        return rows.Select(ToLocal).ToList();
    }

    public async Task<BoardOutboxEntry?> FindOutboxAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Outbox.FindAsync([operationId], cancellationToken);
        return row is null ? null : ToLocal(row);
    }

    public async Task EnqueueOutboxAsync(BoardOutboxEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Outbox.Add(ToRow(entry));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateOutboxAsync(BoardOutboxEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Outbox.FindAsync([entry.OperationId], cancellationToken);
        if (existing is null)
        {
            db.Outbox.Add(ToRow(entry));
        }
        else
        {
            existing.UserKey = entry.UserKey;
            existing.Kind = entry.Kind;
            existing.PayloadJson = entry.PayloadJson;
            existing.CreatedAtUtc = entry.CreatedAtUtc;
            existing.AttemptCount = entry.AttemptCount;
            existing.LastAttemptUtc = entry.LastAttemptUtc;
            existing.LastError = entry.LastError;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteOutboxAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Outbox.FindAsync([operationId], cancellationToken);
        if (existing is not null)
        {
            db.Outbox.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ClearOutboxForUserAsync(string userKey, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Outbox.Where(o => o.UserKey == userKey).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<bool> HasOutboxAsync(string userKey, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Outbox.AnyAsync(o => o.UserKey == userKey, cancellationToken);
    }

    public async Task ClearAllStateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.BoardItems.ExecuteDeleteAsync(cancellationToken);
        await db.Outbox.ExecuteDeleteAsync(cancellationToken);
        var meta = await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken);
        if (meta is null)
        {
            db.Meta.Add(new LocalBoardStoreMetaRow { Id = 1 });
        }
        else
        {
            meta.BoundUserKey = null;
            meta.LastSyncCursorUtc = null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private static BoardLocalRow ToLocal(LocalBoardItemRow row) => new()
    {
        Id = row.Id,
        UserKey = row.UserKey,
        Section = row.Section,
        Title = row.Title,
        IsCompleted = row.IsCompleted,
        Counter = row.Counter,
        Notes = row.Notes,
        Tags = row.Tags,
        TrackPlus = row.TrackPlus,
        TrackMinus = row.TrackMinus,
        NegativeCounter = row.NegativeCounter,
        ResetPeriod = row.ResetPeriod,
        HabitPeriodStart = row.HabitPeriodStart,
        DailyStartDate = row.DailyStartDate,
        DailyRepeat = row.DailyRepeat,
        DailyRepeatInterval = row.DailyRepeatInterval,
        DailyWeekdays = row.DailyWeekdays,
        ChecklistJson = row.ChecklistJson,
        DailyLastCompletedOn = row.DailyLastCompletedOn,
        TodoDueDate = row.TodoDueDate,
        IsArchived = row.IsArchived,
        AwaitingServerCreate = row.AwaitingServerCreate,
        ServerUpdatedAtUtc = row.ServerUpdatedAtUtc,
        CreatedAtUtc = row.CreatedAtUtc,
        SortOrder = row.SortOrder
    };

    private static BoardOutboxEntry ToLocal(BoardOutboxRow row) => new()
    {
        OperationId = row.OperationId,
        UserKey = row.UserKey,
        Kind = row.Kind,
        PayloadJson = row.PayloadJson,
        CreatedAtUtc = row.CreatedAtUtc,
        AttemptCount = row.AttemptCount,
        LastAttemptUtc = row.LastAttemptUtc,
        LastError = row.LastError
    };

    private static LocalBoardItemRow ToRow(BoardLocalRow item) => new()
    {
        Id = item.Id,
        UserKey = item.UserKey,
        Section = item.Section,
        Title = item.Title,
        IsCompleted = item.IsCompleted,
        Counter = item.Counter,
        Notes = item.Notes,
        Tags = item.Tags,
        TrackPlus = item.TrackPlus,
        TrackMinus = item.TrackMinus,
        NegativeCounter = item.NegativeCounter,
        ResetPeriod = item.ResetPeriod,
        HabitPeriodStart = item.HabitPeriodStart,
        DailyStartDate = item.DailyStartDate,
        DailyRepeat = item.DailyRepeat,
        DailyRepeatInterval = item.DailyRepeatInterval,
        DailyWeekdays = item.DailyWeekdays,
        ChecklistJson = item.ChecklistJson,
        DailyLastCompletedOn = item.DailyLastCompletedOn,
        TodoDueDate = item.TodoDueDate,
        IsArchived = item.IsArchived,
        AwaitingServerCreate = item.AwaitingServerCreate,
        ServerUpdatedAtUtc = item.ServerUpdatedAtUtc,
        CreatedAtUtc = item.CreatedAtUtc,
        SortOrder = item.SortOrder
    };

    private static BoardOutboxRow ToRow(BoardOutboxEntry entry) => new()
    {
        OperationId = entry.OperationId,
        UserKey = entry.UserKey,
        Kind = entry.Kind,
        PayloadJson = entry.PayloadJson,
        CreatedAtUtc = entry.CreatedAtUtc,
        AttemptCount = entry.AttemptCount,
        LastAttemptUtc = entry.LastAttemptUtc,
        LastError = entry.LastError
    };

    private static async Task EnsureSqliteBoardColumnsAsync(LocalBoardDbContext db, CancellationToken cancellationToken)
    {
        var boardColumns = await GetTableColumnsAsync(db, "BoardItems", cancellationToken);
        var metaColumns = await GetTableColumnsAsync(db, "Meta", cancellationToken);

        (string Column, string Ddl)[] boardMigrations =
        [
            ("ServerUpdatedAtUtc", "ALTER TABLE BoardItems ADD COLUMN ServerUpdatedAtUtc TEXT NULL;"),
            ("CreatedAtUtc", "ALTER TABLE BoardItems ADD COLUMN CreatedAtUtc TEXT NULL;"),
            ("IsArchived", "ALTER TABLE BoardItems ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;"),
            ("HabitPeriodStart", "ALTER TABLE BoardItems ADD COLUMN HabitPeriodStart TEXT NULL;"),
            ("DailyWeekdays", "ALTER TABLE BoardItems ADD COLUMN DailyWeekdays INTEGER NOT NULL DEFAULT 0;")
        ];

        foreach (var (column, ddl) in boardMigrations)
        {
            if (!boardColumns.Contains(column))
            {
                await db.Database.ExecuteSqlRawAsync(ddl, cancellationToken);
            }
        }

        if (!boardColumns.Contains("SortOrder"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE BoardItems ADD COLUMN SortOrder REAL NULL;",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE BoardItems SET SortOrder = rowid WHERE SortOrder IS NULL;",
                cancellationToken);
        }

        if (!metaColumns.Contains("LastSyncCursorUtc"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Meta ADD COLUMN LastSyncCursorUtc TEXT NULL;",
                cancellationToken);
        }
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(
        LocalBoardDbContext db,
        string table,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = table switch
        {
            "BoardItems" => "PRAGMA table_info(BoardItems);",
            "Meta" => "PRAGMA table_info(Meta);",
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Only internal tables can be inspected.")
        };
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }
}
