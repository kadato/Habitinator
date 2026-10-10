using System.ComponentModel;
using System.Globalization;

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
    [McpServerTool(Name = "get_overview", ReadOnly = true, OpenWorld = false)]
    [Description("Reads activity overview: dashboard plus daily and habit contributions for a period and optional tag.")]
    public async Task<ActivityOverviewDto> GetOverview(
        [Description("Period key such as last_7_days, last_30_days, or null for default.")] string? period = null,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
        => await stats.GetOverviewAsync(McpUser.RequireId(http), period, tag, cancellationToken);

    [McpServerTool(Name = "get_dashboard", ReadOnly = true, OpenWorld = false)]
    [Description("Reads the activity dashboard heatmap data for a period and optional tag.")]
    public async Task<ActivityDashboardDto> GetDashboard(
        [Description("Period key or null for default.")] string? period = null,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
        => await stats.GetDashboardAsync(McpUser.RequireId(http), period, tag, cancellationToken);

    [McpServerTool(Name = "get_daily_contributions", ReadOnly = true, OpenWorld = false)]
    [Description("Reads daily contribution rows for a period and optional tag.")]
    public async Task<DailyContributionsViewDto> GetDailyContributions(
        [Description("Period key or null for default.")] string? period = null,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
        => await stats.GetDailyContributionsAsync(McpUser.RequireId(http), period, tag, cancellationToken);

    [McpServerTool(Name = "get_habit_contributions", ReadOnly = true, OpenWorld = false)]
    [Description("Reads habit contribution rows for a period and optional tag.")]
    public async Task<HabitContributionsViewDto> GetHabitContributions(
        [Description("Period key or null for default.")] string? period = null,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
        => await stats.GetHabitContributionsAsync(McpUser.RequireId(http), period, tag, cancellationToken);

    [McpServerTool(Name = "get_day_detail", ReadOnly = true, OpenWorld = false)]
    [Description("Reads activity detail for one calendar date in yyyy-MM-dd form with an optional tag filter.")]
    public async Task<ActivityDayDetailDto> GetDayDetail(
        [Description("Date as yyyy-MM-dd.")] string date,
        [Description("Tag filter, or null for all.")] string? tag = null,
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new McpProtocolException("Date must use yyyy-MM-dd.", McpErrorCode.InvalidParams);
        }

        return await stats.GetActivityDayDetailAsync(McpUser.RequireId(http), day, tag, cancellationToken);
    }

    [McpServerTool(Name = "log_activity", OpenWorld = true)]
    [Description("Logs one activity event such as a habit tick or daily completion. For focus timer sessions use log_timer_session instead.")]
    public async Task<string> LogActivity(
        [Description("Event type: HabitPlus, HabitMinus, DailyComplete, DailyUncomplete, TodoComplete, TodoUncomplete, DailySkip.")] ActivityEventType eventType,
        [Description("Related board item id, or null.")] Guid? boardItemId = null,
        [Description("Free text label such as Deep work, or null.")] string? customLabel = null,
        [Description("Idempotency key. Reuse it on retry. Null generates one.")] Guid? eventId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (eventType == ActivityEventType.TimerSession)
        {
            throw new McpProtocolException("Use log_timer_session for TimerSession events.", McpErrorCode.InvalidParams);
        }

        await boards.LogActivityAsync(userId, eventType, boardItemId, null, customLabel, eventId, cancellationToken);
        return "Logged.";
    }
}
