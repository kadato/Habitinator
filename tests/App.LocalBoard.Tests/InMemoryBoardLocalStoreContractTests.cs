using App.Shared.RCL.Services.Board.Local;

namespace App.LocalBoard.Tests;

public sealed class InMemoryBoardLocalStoreContractTests : BoardLocalStoreContract
{
    protected override Task<IBoardLocalStore> CreateStoreAsync() =>
        Task.FromResult<IBoardLocalStore>(new InMemoryBoardLocalStore());
}
