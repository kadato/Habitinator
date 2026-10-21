using System.ComponentModel;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class ActivityTools(
    ActivityStatisticsService stats,
    BoardPersistenceService boards,
    IHttpContextAccessor http)
{
    [McpServerTool(Name = "get_overview", Title = "Get activity overview", ReadOnly = true, OpenWorld = false)]
    [Description("Reads activity overview: dashboard plus daily and habit contributions for a period and optional tag.")]
    public async Task<ActivityOverviewDto> GetOverview(
        [Description("Period key such as last_7_days, last_30_days, or null for default.")] string? period = null,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
        => await stats.GetOverviewAsync(McpUser.RequireId(http), period, tag, cancellationToken);

    [McpServerTool(Name = "get_dashboard", Title = "Get activity dashboard", ReadOnly = true, OpenWorld = false)]
    [Description("Reads the activity dashboard heatmap data for a period and optional tag.")]
    public async Task<ActivityDashboardDto> GetDashboard(
        [Description("Period key or null for default.")] string? period = null,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
        => await stats.GetDashboardAsync(McpUser.RequireId(http), period, tag, cancellationToken);

    [McpServerTool(Name = "get_daily_contributions", Title = "Get daily contributions", ReadOnly = true, OpenWorld = false)]
    [Description("Reads daily contribution rows for a period and optional tag.")]
    public async Task<DailyContributionsViewDto> GetDailyContributions(
        [Description("Period key or null for default.")] string? period = null,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
        => await stats.GetDailyContributionsAsync(McpUser.RequireId(http), period, tag, cancellationToken);

    [McpServerTool(Name = "get_habit_contributions", Title = "Get habit contributions", ReadOnly = true, OpenWorld = false)]
    [Description("Reads habit contribution rows for a period and optional tag.")]
    public async Task<HabitContributionsViewDto> GetHabitContributions(
        [Description("Period key or null for default.")] string? period = null,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
        => await stats.GetHabitContributionsAsync(McpUser.RequireId(http), period, tag, cancellationToken);

    [McpServerTool(Name = "get_day_detail", Title = "Get activity day detail", ReadOnly = true, OpenWorld = false)]
    [Description("Reads activity detail for one calendar date in yyyy-MM-dd form with an optional tag filter.")]
    public async Task<ActivityDayDetailDto> GetDayDetail(
        [Description("Date as yyyy-MM-dd.")] string date,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var day = McpInput.ParseDate(date);

        return await stats.GetActivityDayDetailAsync(McpUser.RequireId(http), day, tag, cancellationToken);
    }

    [McpServerTool(Name = "list_activity_events", Title = "List activity events", ReadOnly = true, OpenWorld = false)]
    [Description("Queries activity events across a date range with optional filtering by event type and board item.")]
    public async Task<IReadOnlyList<ActivityDayEventDto>> ListActivityEvents(
        [Description("Start date as yyyy-MM-dd.")] string startDate,
        [Description("End date as yyyy-MM-dd.")] string endDate,
        [Description("Event type filter, or null for all.")] ActivityEventType? eventType = null,
        [Description("Board item ID filter, or null for all.")] Guid? boardItemId = null,
        [Description("Maximum events to return, from 1 to 200. Default is 50.")] int? limit = 50,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var start = McpInput.ParseDate(startDate);
        var end = McpInput.ParseDate(endDate);
        if (start > end)
        {
            throw new McpProtocolException("startDate must be before or equal to endDate.", McpErrorCode.InvalidParams);
        }

        return await stats.GetActivityEventsAsync(userId, start, end, eventType, boardItemId, limit ?? 50, cancellationToken);
    }

    [McpServerTool(Name = "log_activity", Title = "Log activity event", Idempotent = true, OpenWorld = true)]
    [Description("Logs one activity event such as a habit tick or daily completion. Reuse eventId on retry so the server logs the event once. For focus timer sessions, use log_timer_session instead.")]
    public async Task<string> LogActivity(
        [Description("Event type: HabitPlus, HabitMinus, DailyComplete, DailyUncomplete, TodoComplete, TodoUncomplete, DailySkip.")] ActivityEventType eventType,
        [Description("Related board item id, or null.")] Guid? boardItemId = null,
        [Description("Free text label such as Deep work, or null. Max 500 chars.")] string? customLabel = null,
        [Description("Idempotency key. Reuse it on retry. Null generates one.")] Guid? eventId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (eventType == ActivityEventType.TimerSession)
        {
            throw new McpProtocolException("Use log_timer_session for TimerSession events.", McpErrorCode.InvalidParams);
        }

        McpInput.CheckCustomLabel(customLabel);
        if (boardItemId.HasValue && await boards.GetItemAsync(userId, boardItemId.Value, cancellationToken) is null)
        {
            throw new McpProtocolException("Board item not found.", McpErrorCode.InvalidParams);
        }

        await boards.LogActivityAsync(userId, eventType, boardItemId, null, customLabel, eventId, cancellationToken);
        return "Logged.";
    }
}
