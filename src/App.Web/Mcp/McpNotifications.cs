using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

internal static class McpNotifications
{
    public static async Task BoardChangedAsync(
        McpServer server,
        Guid? itemId,
        CancellationToken cancellationToken)
    {
        // Best effort. On stateless Streamable HTTP there is no push channel,
        // so this only reaches stateful clients. SignalR already fans out to the UI.
        try
        {
            await server.SendNotificationAsync(
                NotificationMethods.ResourceUpdatedNotification,
                new ResourceUpdatedNotificationParams { Uri = "board://snapshot" },
                serializerOptions: null,
                cancellationToken: cancellationToken);
            if (itemId.HasValue)
            {
                await server.SendNotificationAsync(
                    NotificationMethods.ResourceUpdatedNotification,
                    new ResourceUpdatedNotificationParams { Uri = $"board://item/{itemId.Value:D}" },
                    serializerOptions: null,
                    cancellationToken: cancellationToken);
            }
        }
#pragma warning disable S2486, S108 // Notifications are best effort on stateless HTTP.
        catch (Exception)
        {
            _ = itemId;
        }
#pragma warning restore S2486, S108
    }
}
