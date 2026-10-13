using System.ComponentModel;

using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class TimerTools(BoardPersistenceService boards, IHttpContextAccessor http)
{
    [McpServerTool(Name = "log_timer_session", Title = "Log focus timer session", Idempotent = true, OpenWorld = true)]
    [Description("Logs a focus timer session of 1 second to 24 hours. Reuse eventId on retry so the server logs the session once. To link the session to a board item, pass boardItemId.")]
    public async Task<string> LogTimerSession(
        [Description("Duration in seconds, 1 to 86400.")] int durationSeconds,
        [Description("Related board item id, or null.")] Guid? boardItemId = null,
        [Description("Free text label such as Deep work, or null. Max 500 chars.")] string? customLabel = null,
        [Description("Idempotency key. Reuse it on retry. Null generates one.")] Guid? eventId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (durationSeconds is < 1 or > 86400)
        {
            throw new McpProtocolException("durationSeconds must be 1 to 86400.", McpErrorCode.InvalidParams);
        }

        McpInput.CheckCustomLabel(customLabel);
        if (boardItemId.HasValue && await boards.GetItemAsync(userId, boardItemId.Value, cancellationToken) is null)
        {
            throw new McpProtocolException("Board item not found.", McpErrorCode.InvalidParams);
        }

        await boards.LogTimerSessionAsync(
            userId,
            TimeSpan.FromSeconds(durationSeconds),
            boardItemId,
            customLabel,
            eventId,
            cancellationToken);
        return $"Logged {durationSeconds}s timer session.";
    }
}
