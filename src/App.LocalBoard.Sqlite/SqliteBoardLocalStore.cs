#pragma warning disable S4158 // existingById fills from a chunked database query. The analyzer cannot track adds through Chunk plus awaited ToDictionaryAsync. The else branch handles new items.
using App.LocalBoard.Sqlite.Data;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Board.Local;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.LocalBoard.Sqlite;

public sealed class SqliteBoardLocalStore(
    IDbContextFactory<LocalBoardDbContext> dbFactory,
    ILogger<SqliteBoardLocalStore> logger) : IBoardLocalStore, IDisposable
{
    /// <summary>Bump when <see cref="EnsureSqliteBoardColumnsAsync" /> gains a migration step.</summary>
    internal const int CurrentSchemaVersion = 1;

    // PRAGMA does not accept parameters. Keep this in sync with CurrentSchemaVersion.
    private const string SetSchemaVersionSql = "PRAGMA user_version = 1;";

    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private volatile bool _schemaReady;

    public void Dispose()
    {
        _schemaLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaLock.WaitAsync(cancellationToken);
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
                await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("PRAGMA cache_size=-64000;", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogTrace(ex, "Failed to enable WAL mode.");
            }

            await EnsureSqliteBoardColumnsAsync(db, cancellationToken);
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    public async Task<BoardStoreMeta> GetMetaAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var meta = await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken);
        return new BoardStoreMeta
        {
            BoundUserKey = meta?.BoundUserKey,
            LastSyncCursorUtc = meta?.LastSyncCursorUtc,
            LastFullMirrorUtc = TryParseMirrorUtc(meta?.LastFullMirrorUtc)
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
                LastSyncCursorUtc = meta.LastSyncCursorUtc,
                LastFullMirrorUtc = FormatMirrorUtc(meta.LastFullMirrorUtc)
            });
        }
        else
        {
            row.BoundUserKey = meta.BoundUserKey;
            row.LastSyncCursorUtc = meta.LastSyncCursorUtc;
            row.LastFullMirrorUtc = FormatMirrorUtc(meta.LastFullMirrorUtc);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset? TryParseMirrorUtc(string? raw) =>
        DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static string? FormatMirrorUtc(DateTimeOffset? value) =>
        value?.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

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
        await UpsertItemCoreAsync(db, row, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task UpsertItemCoreAsync(
        LocalBoardDbContext db,
        BoardLocalRow row,
        CancellationToken cancellationToken)
    {
        var existing = await db.BoardItems.FirstOrDefaultAsync(x => x.Id == row.Id && x.UserKey == row.UserKey, cancellationToken);
        if (existing is null)
        {
            // Guard against a stale row from another user reusing the same id.
            var foreign = await db.BoardItems.FirstOrDefaultAsync(x => x.Id == row.Id, cancellationToken);
            if (foreign is not null)
            {
                db.BoardItems.Remove(foreign);
            }

            db.BoardItems.Add(ToRow(row));
        }
        else
        {
            existing.Section = row.Section;
            existing.CopyFrom(ToRow(row));
            existing.AwaitingServerCreate = row.AwaitingServerCreate;
        }
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
            const int chunkSize = 500;
            for (var i = 0; i < rows.Count; i += chunkSize)
            {
                var chunk = rows.Skip(i).Take(chunkSize).Select(ToRow).ToList();
                db.BoardItems.AddRange(chunk);
                await db.SaveChangesAsync(cancellationToken);
            }

            var meta = await db.Meta.SingleOrDefaultAsync(m => m.Id == 1, cancellationToken);
            if (meta is null)
            {
                db.Meta.Add(new LocalBoardStoreMetaRow { Id = 1, LastSyncCursorUtc = cursor });
            }
            else
            {
                meta.LastSyncCursorUtc = cursor;
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
            foreach (var idChunk in Chunk(deletedIds, 500))
            {
                await db.BoardItems
                    .Where(x => x.UserKey == userKey && idChunk.Contains(x.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            if (upserts.Count > 0)
            {
                var ids = upserts.Select(u => u.Item.Id).Distinct().ToList();
                var existingById = new Dictionary<Guid, LocalBoardItemRow>();
                foreach (var idChunk in Chunk(ids, 500))
                {
                    var chunkRows = await db.BoardItems
                        .Where(x => x.UserKey == userKey && idChunk.Contains(x.Id))
                        .ToDictionaryAsync(x => x.Id, cancellationToken);
                    foreach (var kv in chunkRows)
                    {
                        existingById[kv.Key] = kv.Value;
                    }
                }

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

    public async Task ApplyItemMutationAsync(
        BoardLocalRow row,
        bool deleteItem,
        BoardOutboxEntry entry,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (deleteItem)
            {
                await db.BoardItems
                    .Where(x => x.UserKey == row.UserKey && x.Id == row.Id)
                    .ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                await UpsertItemCoreAsync(db, row, cancellationToken);
            }

            db.Outbox.Add(ToRow(entry));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch
            {
                // Keep the original failure. Dispose still rolls back.
            }

            throw;
        }
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
            meta.LastFullMirrorUtc = null;
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
        TargetCount = row.TargetCount,
        ResetPeriod = row.ResetPeriod,
        HabitPeriodStart = row.HabitPeriodStart,
        DailyStartDate = row.DailyStartDate,
        DailyRepeat = row.DailyRepeat,
        DailyRepeatInterval = row.DailyRepeatInterval,
        DailyWeekdays = row.DailyWeekdays,
        ChecklistJson = row.ChecklistJson,
        DailyLastCompletedOn = row.DailyLastCompletedOn,
        DailyPostponedUntil = row.DailyPostponedUntil,
        DailyPostponedFrom = row.DailyPostponedFrom,
        DailySkippedOn = row.DailySkippedOn,
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
        TargetCount = item.TargetCount,
        ResetPeriod = item.ResetPeriod,
        HabitPeriodStart = item.HabitPeriodStart,
        DailyStartDate = item.DailyStartDate,
        DailyRepeat = item.DailyRepeat,
        DailyRepeatInterval = item.DailyRepeatInterval,
        DailyWeekdays = item.DailyWeekdays,
        ChecklistJson = item.ChecklistJson,
        DailyLastCompletedOn = item.DailyLastCompletedOn,
        DailyPostponedUntil = item.DailyPostponedUntil,
        DailyPostponedFrom = item.DailyPostponedFrom,
        DailySkippedOn = item.DailySkippedOn,
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
        var schemaVersion = await GetSchemaVersionAsync(db, cancellationToken);
        if (schemaVersion < CurrentSchemaVersion)
        {
            var boardColumns = await GetTableColumnsAsync(db, "BoardItems", cancellationToken);
            var metaColumns = await GetTableColumnsAsync(db, "Meta", cancellationToken);

            (string Column, string Ddl)[] boardMigrations =
            [
                ("ServerUpdatedAtUtc", "ALTER TABLE BoardItems ADD COLUMN ServerUpdatedAtUtc TEXT NULL;"),
                ("CreatedAtUtc", "ALTER TABLE BoardItems ADD COLUMN CreatedAtUtc TEXT NULL;"),
                ("IsArchived", "ALTER TABLE BoardItems ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;"),
                ("HabitPeriodStart", "ALTER TABLE BoardItems ADD COLUMN HabitPeriodStart TEXT NULL;"),
                ("DailyWeekdays", "ALTER TABLE BoardItems ADD COLUMN DailyWeekdays INTEGER NOT NULL DEFAULT 0;"),
                ("DailyPostponedUntil", "ALTER TABLE BoardItems ADD COLUMN DailyPostponedUntil TEXT NULL;"),
                ("DailyPostponedFrom", "ALTER TABLE BoardItems ADD COLUMN DailyPostponedFrom TEXT NULL;"),
                ("TargetCount", "ALTER TABLE BoardItems ADD COLUMN TargetCount INTEGER NULL;"),
                ("DailySkippedOn", "ALTER TABLE BoardItems ADD COLUMN DailySkippedOn TEXT NULL;")
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

            if (!metaColumns.Contains("LastFullMirrorUtc"))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE Meta ADD COLUMN LastFullMirrorUtc TEXT NULL;",
                    cancellationToken);
            }
        }

        // Indexes are idempotent and cheap. Keep them outside the version gate so
        // a database restored from an odd state still gets the expected indexes.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_BoardItems_UserKey_Section ON BoardItems (UserKey, Section);",
            cancellationToken);
        // Older builds created a second index over the same columns under a
        // different name. Drop it before ensuring the model index exists.
        await db.Database.ExecuteSqlRawAsync(
            "DROP INDEX IF EXISTS IX_Outbox_UserKey_Created;",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_Outbox_UserKey_CreatedAtUtc ON Outbox (UserKey, CreatedAtUtc);",
            cancellationToken);

        if (schemaVersion < CurrentSchemaVersion)
        {
            await db.Database.ExecuteSqlRawAsync(SetSchemaVersionSql, cancellationToken);
        }
    }

    private static async Task<int> GetSchemaVersionAsync(LocalBoardDbContext db, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is null ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(Math.Min(size, source.Count - i)).ToList();
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
