using App.Shared.RCL.Hubs;
using App.Web.Hubs;

using Microsoft.AspNetCore.SignalR;

namespace App.Web.Services;

public interface IBoardChangeNotifier
{
    Task NotifyBoardChangedAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class BoardChangeNotifier : IBoardChangeNotifier
{
    private readonly IHubContext<BoardHub> _hub;

    public BoardChangeNotifier(IHubContext<BoardHub> hub)
    {
        _hub = hub;
    }

    public async Task NotifyBoardChangedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await _hub.Clients
            .Group(BoardHub.UserGroupName(userId))
            .SendAsync(BoardHubClient.BoardChanged, cancellationToken);
    }
}
