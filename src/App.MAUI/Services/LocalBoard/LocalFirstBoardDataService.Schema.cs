using App.MAUI.Data;

using Microsoft.EntityFrameworkCore;

namespace App.MAUI.Services.LocalBoard;

public sealed partial class LocalFirstBoardDataService
{
    private static async Task EnsureSqliteBoardColumnsAsync(LocalBoardDbContext db, CancellationToken cancellationToken)
    {
        var boardColumns = await GetTableColumnsAsync(db, "BoardItems", cancellationToken);
        var metaColumns = await GetTableColumnsAsync(db, "Meta", cancellationToken);

        (string Column, string Ddl)[] boardMigrations =
        [
            ("ServerUpdatedAtUtc", "ALTER TABLE BoardItems ADD COLUMN ServerUpdatedAtUtc TEXT NULL;"),
            ("CreatedAtUtc", "ALTER TABLE BoardItems ADD COLUMN CreatedAtUtc TEXT NULL;"),
            ("IsArchived", "ALTER TABLE BoardItems ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;"),
            ("TodoRepeatIntervalDays", "ALTER TABLE BoardItems ADD COLUMN TodoRepeatIntervalDays INTEGER NULL;")
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
