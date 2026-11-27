using App.LocalBoard.Sqlite;
using App.LocalBoard.Sqlite.Data;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.LocalBoard.Tests;

/// <summary>
///     Covers the hand-written SQLite upgrade path. A local mirror created by an
///     older app version must open after an upgrade without losing rows.
/// </summary>
public sealed class SqliteSchemaUpgradeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "habitinator-sqlite-upgrade-tests");
    private readonly List<string> _paths = [];

    [Fact]
    public async Task EnsureReady_UpgradesLegacySchema_AndPreservesRows()
    {
        var path = CreateDatabase("legacy");
        await CreateLegacySchemaAsync(path);

        var factory = new TestLocalBoardDbContextFactory(path);
        var store = new SqliteBoardLocalStore(factory, NullLogger<SqliteBoardLocalStore>.Instance);
        await store.EnsureReadyAsync();
        store.Dispose();

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();

            var columns = await ReadColumnNamesAsync(connection, "BoardItems");
            columns.Should().Contain(["ServerUpdatedAtUtc", "CreatedAtUtc", "IsArchived", "HabitPeriodStart",
                "DailyWeekdays", "DailyPostponedUntil", "DailyPostponedFrom", "TargetCount", "DailySkippedOn",
                "SortOrder"]);

            var metaColumns = await ReadColumnNamesAsync(connection, "Meta");
            metaColumns.Should().Contain(["LastSyncCursorUtc", "LastFullMirrorUtc"]);

            await using (var row = connection.CreateCommand())
            {
                row.CommandText = "SELECT Title, SortOrder FROM BoardItems WHERE Id = '11111111-1111-1111-1111-111111111111';";
                await using var reader = await row.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                reader.GetString(0).Should().Be("Legacy habit");
                reader.GetDouble(1).Should().Be(1);
            }

            SqliteSchemaUpgradeTests.ReadUserVersion(connection).Should().Be(SqliteBoardLocalStore.CurrentSchemaVersion);
            var boardIndexes = await ReadIndexNamesAsync(connection, "BoardItems");
            boardIndexes.Should().Contain("IX_BoardItems_UserKey_Section");
            var outboxIndexes = await ReadIndexNamesAsync(connection, "Outbox");
            outboxIndexes.Should().Contain("IX_Outbox_UserKey_CreatedAtUtc");
            outboxIndexes.Should().NotContain("IX_Outbox_UserKey_Created");
        }

        // Running the upgrade twice must be a no-op.
        using var reopened = new SqliteBoardLocalStore(factory, NullLogger<SqliteBoardLocalStore>.Instance);
        await reopened.EnsureReadyAsync();

        await using var verify = new SqliteConnection($"Data Source={path}");
        await verify.OpenAsync();
        SqliteSchemaUpgradeTests.ReadUserVersion(verify).Should().Be(SqliteBoardLocalStore.CurrentSchemaVersion);
    }

    [Fact]
    public async Task EnsureReady_NewDatabase_RecordsCurrentVersion()
    {
        var path = CreateDatabase("fresh");
        using var store = new SqliteBoardLocalStore(
            new TestLocalBoardDbContextFactory(path),
            NullLogger<SqliteBoardLocalStore>.Instance);
        await store.EnsureReadyAsync();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        ReadUserVersion(connection).Should().Be(SqliteBoardLocalStore.CurrentSchemaVersion);
    }

    private string CreateDatabase(string name)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{name}-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return path;
    }

    private static async Task CreateLegacySchemaAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE BoardItems (
                Id TEXT NOT NULL PRIMARY KEY,
                UserKey TEXT NOT NULL,
                Section TEXT NOT NULL,
                Title TEXT NOT NULL,
                IsCompleted INTEGER NOT NULL,
                Counter INTEGER NOT NULL,
                NegativeCounter INTEGER NOT NULL,
                TrackPlus INTEGER NOT NULL,
                TrackMinus INTEGER NOT NULL,
                ResetPeriod INTEGER NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            INSERT INTO BoardItems (
                Id, UserKey, Section, Title, IsCompleted, Counter, NegativeCounter,
                TrackPlus, TrackMinus, ResetPeriod, UpdatedAtUtc)
            VALUES (
                '11111111-1111-1111-1111-111111111111', 'user@test.local', 'Habit', 'Legacy habit',
                0, 2, 0, 1, 1, 0, '2026-01-01T00:00:00+00:00');

            CREATE TABLE Meta (
                Id INTEGER NOT NULL PRIMARY KEY,
                BoundUserKey TEXT NULL
            );

            CREATE TABLE Outbox (
                OperationId TEXT NOT NULL PRIMARY KEY,
                UserKey TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                PayloadJson TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                AttemptCount INTEGER NOT NULL,
                LastAttemptUtc TEXT NULL,
                LastError TEXT NULL
            );

            CREATE INDEX IX_Outbox_UserKey_Created ON Outbox (UserKey, CreatedAtUtc);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(SqliteConnection connection, string table)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{table}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static async Task<HashSet<string>> ReadIndexNamesAsync(SqliteConnection connection, string table)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list('{table}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try
                {
                    File.Delete(path + suffix);
                }
                catch (IOException)
                {
                    // A failed cleanup must not fail the test run.
                }
            }
        }
    }
}
