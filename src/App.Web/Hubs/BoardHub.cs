using App.Web.Auth;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace App.Web.Hubs;

[Authorize("BoardOrJwt")]
public sealed class BoardHub : Hub
{
    public static string UserGroupName(Guid userId)
    {
        return $"board-user:{userId:D}";
    }

    public override async Task OnConnectedAsync()
    {
        if (AuthenticatedUserId.TryGet(Context.User) is { } userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(userId));
        }

        await base.OnConnectedAsync();
    }
}
