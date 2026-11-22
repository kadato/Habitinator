using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Board.Local;
using App.Web.Client.Services;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace App.LocalBoard.Tests;

public sealed class IndexedDbBoardLocalStoreContractTests : BoardLocalStoreContract
{
    private readonly FakeJsRuntime _js = new();

    protected override Task<IBoardLocalStore> CreateStoreAsync() => CreateStore();

    [Fact]
    public async Task PersistedState_RehydratesInANewStore()
    {
        var id = Guid.NewGuid();
        await Store.UpsertItemAsync(new BoardLocalRow
        {
            Id = id,
            UserKey = "alice",
            Section = BoardSection.Habit,
            Title = "persisted",
            Counter = 4
        });
        await Store.SetMetaAsync(new BoardStoreMeta { BoundUserKey = "alice" });
        await Store.FlushAsync();

        var reloaded = await CreateStore();
        await reloaded.EnsureReadyAsync();

        var row = await reloaded.FindItemAsync("alice", id);
        row.Should().BeEquivalentTo(new { Title = "persisted", Counter = 4 });
        (await reloaded.GetMetaAsync()).BoundUserKey.Should().Be("alice");
    }

    private Task<IBoardLocalStore> CreateStore() =>
        Task.FromResult<IBoardLocalStore>(new IndexedDbBoardLocalStore(
            _js,
            NullLogger<IndexedDbBoardLocalStore>.Instance));
}
