using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

internal static class McpProgress
{
    public static async Task ReportAsync(
        McpServer server,
        RequestContext<CallToolRequestParams>? ctx,
        float progress,
        float? total,
        string message,
        CancellationToken cancellationToken)
    {
        var token = ctx?.Params?.ProgressToken;
        if (token is null)
        {
            return;
        }

        try
        {
            await server.SendNotificationAsync(
                NotificationMethods.ProgressNotification,
                new ProgressNotificationParams
                {
                    ProgressToken = token.Value,
                    Progress = new ProgressNotificationValue
                    {
                        Progress = progress,
                        Total = total,
                        Message = message,
                    },
                },
                serializerOptions: null,
                cancellationToken: cancellationToken);
        }
#pragma warning disable S2486, S108 // Progress is best effort. Stateless clients may not listen.
        catch (Exception)
        {
            _ = message;
        }
#pragma warning restore S2486, S108
    }
}
