using App.Shared.RCL.Models;
using App.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

public sealed class BoardPersistenceService
{
    private readonly ApplicationDbContext _dbContext;

    public BoardPersistenceService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BoardSnapshot> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        List<BoardItemEntity> items = await _dbContext.BoardItems
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new BoardSnapshot(
            items.Where(x => x.Section == BoardSection.Habit).OrderBy(x => x.CreatedAtUtc).Select(ToModel).ToList(),
            items.Where(x => x.Section == BoardSection.Daily).OrderBy(x => x.IsCompleted).ThenBy(x => x.CreatedAtUtc).Select(ToModel).ToList(),
            items.Where(x => x.Section == BoardSection.Todo).OrderBy(x => x.IsCompleted).ThenBy(x => x.CreatedAtUtc).Select(ToModel).ToList());
    }

    public async Task<BoardItem> CreateItemAsync(Guid userId, BoardSection section, string title, CancellationToken cancellationToken = default)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var entity = new BoardItemEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Section = section,
            Title = title,
            IsCompleted = false,
            Counter = 0,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };

        _dbContext.BoardItems.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<BoardItem?> RenameItemAsync(Guid userId, BoardSection section, Guid itemId, string title, CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == section && x.Id == itemId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Title = title;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<bool> DeleteItemAsync(Guid userId, BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == section && x.Id == itemId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        _dbContext.BoardItems.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<BoardItem?> ToggleItemAsync(Guid userId, BoardSection section, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == section && x.Id == itemId, cancellationToken);
        if (entity is null || section == BoardSection.Habit)
        {
            return null;
        }

        entity.IsCompleted = !entity.IsCompleted;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<BoardItem?> IncrementHabitAsync(Guid userId, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Counter++;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task<BoardItem?> DecrementHabitAsync(Guid userId, Guid itemId, CancellationToken cancellationToken = default)
    {
        BoardItemEntity? entity = await _dbContext.BoardItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Section == BoardSection.Habit && x.Id == itemId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Counter = Math.Max(0, entity.Counter - 1);
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(entity);
    }

    public async Task SeedBoardDataIfMissingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        bool hasItems = await _dbContext.BoardItems.AnyAsync(x => x.UserId == userId, cancellationToken);
        if (hasItems)
        {
            return;
        }

        var utcNow = DateTimeOffset.UtcNow;
        _dbContext.BoardItems.AddRange(
            new BoardItemEntity { Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Habit, Title = "Drink a glass of water", Counter = 3, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow },
            new BoardItemEntity { Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Habit, Title = "Read 10 pages", Counter = 1, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow },
            new BoardItemEntity { Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Daily, Title = "Workout", IsCompleted = false, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow },
            new BoardItemEntity { Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Daily, Title = "Deep work block", IsCompleted = true, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow },
            new BoardItemEntity { Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Todo, Title = "Submit assignment", IsCompleted = false, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow },
            new BoardItemEntity { Id = Guid.NewGuid(), UserId = userId, Section = BoardSection.Todo, Title = "Call advisor", IsCompleted = false, CreatedAtUtc = utcNow, UpdatedAtUtc = utcNow }
        );

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static BoardItem ToModel(BoardItemEntity entity) =>
        new(entity.Id, entity.Title, entity.IsCompleted, entity.Counter);
}
