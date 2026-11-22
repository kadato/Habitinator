using App.LocalBoard.Sqlite;
using App.LocalBoard.Sqlite.Data;
using App.Shared.RCL.Services.Board.Local;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.LocalBoard.Tests;

public sealed class SqliteBoardLocalStoreContractTests : BoardLocalStoreContract
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "habitinator-localboard-tests");
    private string _databasePath = "";

    protected override Task<IBoardLocalStore> CreateStoreAsync()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, $"{Guid.NewGuid():N}.db");
        var store = new SqliteBoardLocalStore(
            new TestLocalBoardDbContextFactory(_databasePath),
            NullLogger<SqliteBoardLocalStore>.Instance);
        return Task.FromResult<IBoardLocalStore>(store);
    }

    protected override Task DisposeStoreAsync(IBoardLocalStore store)
    {
        (store as IDisposable)?.Dispose();
        DeleteDatabaseFiles();
        return Task.CompletedTask;
    }

    private void DeleteDatabaseFiles()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

internal sealed class TestLocalBoardDbContextFactory : IDbContextFactory<LocalBoardDbContext>
{
    private readonly DbContextOptions<LocalBoardDbContext> _options;

    public TestLocalBoardDbContextFactory(string databasePath)
    {
        _options = new DbContextOptionsBuilder<LocalBoardDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
    }

    public LocalBoardDbContext CreateDbContext() => new(_options);
}
