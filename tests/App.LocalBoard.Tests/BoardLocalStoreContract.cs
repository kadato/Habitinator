using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Board.Local;

using FluentAssertions;

namespace App.LocalBoard.Tests;

/// <summary>
///     Shared behavior every <see cref="IBoardLocalStore" /> implementation must provide.
///     Derived classes supply the concrete store, and the suite runs unchanged against each one.
/// </summary>
public abstract class BoardLocalStoreContract : IAsyncLifetime
{
    private IBoardLocalStore _store = null!;

    protected IBoardLocalStore Store => _store;

    public async Task InitializeAsync()
    {
        _store = await CreateStoreAsync();
        await _store.EnsureReadyAsync();
        await _store.ClearAllStateAsync();
        await _store.FlushAsync();
    }

    public async Task DisposeAsync()
    {
        await Store.FlushAsync();
        await DisposeStoreAsync(Store);
    }

    protected abstract Task<IBoardLocalStore> CreateStoreAsync();

    protected virtual Task DisposeStoreAsync(IBoardLocalStore store)
    {
        (store as IDisposable)?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EnsureReady_IsIdempotent()
    {
        await Store.EnsureReadyAsync();
        await Store.EnsureReadyAsync();

        (await Store.GetMetaAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task Meta_RoundTrips()
    {
        var stamp = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
        await Store.SetMetaAsync(new BoardStoreMeta
        {
            BoundUserKey = "alice@example.com",
            LastSyncCursorUtc = "cursor-1",
            LastFullMirrorUtc = stamp
        });

        var meta = await Store.GetMetaAsync();

        meta.BoundUserKey.Should().Be("alice@example.com");
        meta.LastSyncCursorUtc.Should().Be("cursor-1");
        meta.LastFullMirrorUtc.Should().Be(stamp);
    }

    [Fact]
    public async Task Items_AreScopedByUserKey()
    {
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        await Store.UpsertItemAsync(Row("alice", aliceId));
        await Store.UpsertItemAsync(Row("bob", bobId));

        var aliceItems = await Store.ListItemsAsync("alice", includeArchived: true);

        aliceItems.Should().ContainSingle().Which.Id.Should().Be(aliceId);
        (await Store.FindItemAsync("alice", bobId)).Should().BeNull();
        (await Store.FindItemAsync("alice", aliceId)).Should().NotBeNull();
    }

    [Fact]
    public async Task ListItems_HidesArchivedUnlessRequested()
    {
        var activeId = Guid.NewGuid();
        var archivedId = Guid.NewGuid();
        await Store.UpsertItemAsync(Row("alice", activeId));
        await Store.UpsertItemAsync(Row("alice", archivedId, archived: true));

        var active = await Store.ListItemsAsync("alice", includeArchived: false);
        var all = await Store.ListItemsAsync("alice", includeArchived: true);

        active.Should().ContainSingle().Which.Id.Should().Be(activeId);
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpsertItem_ReplacesExistingRow()
    {
        var id = Guid.NewGuid();
        await Store.UpsertItemAsync(new BoardLocalRow
        {
            Id = id,
            UserKey = "alice",
            Section = BoardSection.Daily,
            Title = "before",
            Counter = 1,
            SortOrder = 1.5,
            AwaitingServerCreate = true
        });

        await Store.UpsertItemAsync(new BoardLocalRow
        {
            Id = id,
            UserKey = "alice",
            Section = BoardSection.Daily,
            Title = "after",
            Counter = 2,
            Notes = "notes",
            Tags = "a,b",
            DailyStartDate = new DateOnly(2026, 2, 3),
            TodoDueDate = new DateOnly(2026, 2, 4),
            IsArchived = true,
            SortOrder = 2.5,
            AwaitingServerCreate = true
        });

        var rows = await Store.ListItemsAsync("alice", includeArchived: true);

        var stored = rows.Should().ContainSingle().Subject;
        stored.Title.Should().Be("after");
        stored.Counter.Should().Be(2);
        stored.Notes.Should().Be("notes");
        stored.Tags.Should().Be("a,b");
        stored.DailyStartDate.Should().Be(new DateOnly(2026, 2, 3));
        stored.TodoDueDate.Should().Be(new DateOnly(2026, 2, 4));
        stored.IsArchived.Should().BeTrue();
        stored.SortOrder.Should().Be(2.5);
        stored.AwaitingServerCreate.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertItem_MovesRowToTheNewUser()
    {
        var id = Guid.NewGuid();
        await Store.UpsertItemAsync(Row("alice", id));
        await Store.UpsertItemAsync(Row("bob", id));

        (await Store.FindItemAsync("alice", id)).Should().BeNull();
        (await Store.FindItemAsync("bob", id)).Should().NotBeNull();
        (await Store.ListItemsAsync("alice", includeArchived: true)).Should().BeEmpty();
        (await Store.ListItemsAsync("bob", includeArchived: true)).Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteItem_IsScopedToUser()
    {
        var id = Guid.NewGuid();
        await Store.UpsertItemAsync(Row("bob", id));

        await Store.DeleteItemAsync("alice", id);
        (await Store.FindItemAsync("bob", id)).Should().NotBeNull();

        await Store.DeleteItemAsync("bob", id);
        (await Store.FindItemAsync("bob", id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAllItems_IsScopedToUser()
    {
        await Store.UpsertItemAsync(Row("alice", Guid.NewGuid()));
        await Store.UpsertItemAsync(Row("alice", Guid.NewGuid()));
        await Store.UpsertItemAsync(Row("bob", Guid.NewGuid()));

        await Store.DeleteAllItemsAsync("alice");

        (await Store.ListItemsAsync("alice", includeArchived: true)).Should().BeEmpty();
        (await Store.ListItemsAsync("bob", includeArchived: true)).Should().ContainSingle();
    }

    [Fact]
    public async Task ReplaceAllItems_ReplacesOneUserAndSetsCursor()
    {
        await Store.SetMetaAsync(new BoardStoreMeta
        {
            BoundUserKey = "alice",
            LastSyncCursorUtc = "old",
            LastFullMirrorUtc = DateTimeOffset.UtcNow
        });
        await Store.UpsertItemAsync(Row("alice", Guid.NewGuid()));
        var bobId = Guid.NewGuid();
        await Store.UpsertItemAsync(Row("bob", bobId));
        var replacement = Row("alice", Guid.NewGuid());

        await Store.ReplaceAllItemsAsync("alice", [replacement], "cursor-2");

        (await Store.ListItemsAsync("alice", includeArchived: true))
            .Should().ContainSingle().Which.Id.Should().Be(replacement.Id);
        (await Store.ListItemsAsync("bob", includeArchived: true))
            .Should().ContainSingle().Which.Id.Should().Be(bobId);
        (await Store.GetMetaAsync()).LastSyncCursorUtc.Should().Be("cursor-2");
    }

    [Fact]
    public async Task ApplyDelta_DeletesUpsertsAndPreservesAwaitingCreate()
    {
        var existingId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var createdId = Guid.NewGuid();
        await Store.UpsertItemAsync(new BoardLocalRow
        {
            Id = existingId,
            UserKey = "alice",
            Section = BoardSection.Habit,
            Title = "old",
            Counter = 1,
            AwaitingServerCreate = true,
            SortOrder = 1
        });
        await Store.UpsertItemAsync(Row("alice", deletedId));

        var updated = new BoardItem(existingId, "new", Counter: 7, SortOrder: 3);
        var created = new BoardItem(createdId, "created", SortOrder: 4);
        await Store.ApplyDeltaAsync(
            "alice",
            [(BoardSection.Habit, updated), (BoardSection.Daily, created)],
            [deletedId]);

        var rows = await Store.ListItemsAsync("alice", includeArchived: true);

        rows.Should().HaveCount(2);
        var existing = rows.Should().ContainSingle(r => r.Id == existingId).Subject;
        existing.Title.Should().Be("new");
        existing.Counter.Should().Be(7);
        existing.AwaitingServerCreate.Should().BeTrue();

        var added = rows.Should().ContainSingle(r => r.Id == createdId).Subject;
        added.Section.Should().Be(BoardSection.Daily);
        added.Title.Should().Be("created");
        added.AwaitingServerCreate.Should().BeFalse();

        (await Store.FindItemAsync("alice", deletedId)).Should().BeNull();
    }

    [Fact]
    public async Task Outbox_LifecycleIsScopedAndUpserts()
    {
        var first = Entry("alice");
        var second = Entry("alice");
        var bobEntry = Entry("bob");
        await Store.EnqueueOutboxAsync(first);
        await Store.EnqueueOutboxAsync(second);
        await Store.EnqueueOutboxAsync(bobEntry);

        (await Store.ListOutboxAsync("alice")).Should().HaveCount(2);
        (await Store.HasOutboxAsync("alice")).Should().BeTrue();
        (await Store.HasOutboxAsync("carol")).Should().BeFalse();

        first.AttemptCount = 3;
        first.LastError = "boom";
        await Store.UpdateOutboxAsync(first);
        (await Store.FindOutboxAsync(first.OperationId)).Should().BeEquivalentTo(first);

        await Store.DeleteOutboxAsync(first.OperationId);
        (await Store.FindOutboxAsync(first.OperationId)).Should().BeNull();
        (await Store.FindOutboxAsync(Guid.NewGuid())).Should().BeNull();

        await Store.ClearOutboxForUserAsync("alice");
        (await Store.ListOutboxAsync("alice")).Should().BeEmpty();
        (await Store.ListOutboxAsync("bob")).Should().ContainSingle()
            .Which.OperationId.Should().Be(bobEntry.OperationId);
    }

    [Fact]
    public async Task ClearAllState_RemovesRowsOutboxAndBinding()
    {
        await Store.SetMetaAsync(new BoardStoreMeta
        {
            BoundUserKey = "alice",
            LastSyncCursorUtc = "cursor",
            LastFullMirrorUtc = DateTimeOffset.UtcNow
        });
        await Store.UpsertItemAsync(Row("alice", Guid.NewGuid()));
        await Store.EnqueueOutboxAsync(Entry("alice"));

        await Store.ClearAllStateAsync();

        (await Store.ListItemsAsync("alice", includeArchived: true)).Should().BeEmpty();
        (await Store.ListOutboxAsync("alice")).Should().BeEmpty();
        var meta = await Store.GetMetaAsync();
        meta.BoundUserKey.Should().BeNull();
        meta.LastSyncCursorUtc.Should().BeNull();
        meta.LastFullMirrorUtc.Should().BeNull();
    }

    [Fact]
    public async Task Flush_AfterWrites_KeepsState()
    {
        var id = Guid.NewGuid();
        await Store.UpsertItemAsync(Row("alice", id));

        await Store.FlushAsync();

        (await Store.FindItemAsync("alice", id)).Should().NotBeNull();
    }

    private static BoardLocalRow Row(string userKey, Guid id, bool archived = false) => new()
    {
        Id = id,
        UserKey = userKey,
        Section = BoardSection.Habit,
        Title = $"item-{id:N}",
        SortOrder = id.GetHashCode(),
        IsArchived = archived
    };

    private static BoardOutboxEntry Entry(string userKey) => new()
    {
        OperationId = Guid.NewGuid(),
        UserKey = userKey,
        Kind = BoardOutboxOperationKind.Create,
        PayloadJson = """{"title":"item"}""",
        CreatedAtUtc = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc)
    };
}
