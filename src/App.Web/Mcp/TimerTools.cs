using System.ComponentModel;

using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class TimerTools(
    BoardPersistenceService boards,
    ActivityStatisticsService stats,
    IHttpContextAccessor http)
{
    [McpServerTool(Name = "get_timer_summary", Title = "Get focus timer summary", ReadOnly = true, OpenWorld = false)]
    [Description("Returns a summary of focus timer sessions for a date: total focus minutes, session count, and breakdown. Defaults to today.")]
    public async Task<TimerSummaryDto> GetTimerSummary(
        [Description("Date as yyyy-MM-dd. Defaults to today.")] string? date = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        DateOnly day = date is not null ? McpInput.ParseDate(date) : await boards.TodayAsync(userId, cancellationToken);
        return await stats.GetTimerSummaryAsync(userId, day, cancellationToken);
    }

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
