using System.ComponentModel;

using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class TimerTools(BoardPersistenceService boards, IHttpContextAccessor http)
{
    [McpServerTool(Name = "log_timer_session", OpenWorld = true)]
    [Description("Logs a focus timer session that lasts 1 second to 24 hours. Optionally links it to a board item.")]
    public async Task<string> LogTimerSession(
        [Description("Duration in seconds, 1 to 86400.")] int durationSeconds,
        [Description("Related board item id, or null.")] Guid? boardItemId = null,
        [Description("Free text label such as Deep work, or null.")] string? customLabel = null,
        [Description("Idempotency key. Reuse it on retry. Null generates one.")] Guid? eventId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (durationSeconds is < 1 or > 86400)
        {
            throw new McpProtocolException("durationSeconds must be 1 to 86400.", McpErrorCode.InvalidParams);
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
