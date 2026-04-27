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
    private readonly ILogger<BoardChangeNotifier> _logger;

    public BoardChangeNotifier(IHubContext<BoardHub> hub, ILogger<BoardChangeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyBoardChangedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Use SendCoreAsync with an empty arg list. SendAsync(hub, cancelToken) can bind to
        // SendAsync(string, object? arg1, CancellationToken) and pass the token as a hub payload, which
        // can fail serialization and surface as random save / API errors after DB commit.
        try
        {
            await _hub.Clients
                .Group(BoardHub.UserGroupName(userId))
                .SendCoreAsync(BoardHubClient.BoardChanged, [], cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR board change notification failed for user {UserId}.", userId);
        }
    }
}
