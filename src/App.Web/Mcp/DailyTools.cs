using System.ComponentModel;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class DailyTools(BoardPersistenceService boards, IHttpContextAccessor http)
{
    [McpServerTool(Name = "daily_complete_for_date", Title = "Complete daily for date", OpenWorld = true)]
    [Description("Marks a daily complete for a past calendar date in yyyy-MM-dd form. For today, use toggle_item instead. Returns the updated daily. Throws when not found.")]
    public async Task<BoardItem> CompleteForDate(
        [Description("Daily item id.")] Guid itemId,
        [Description("Date as yyyy-MM-dd.")] string date,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var day = McpInput.ParseDate(date);

        var result = await boards.CompleteDailyForDateAsync(userId, itemId, day, null, cancellationToken);
        return result.Item
            ?? throw new McpProtocolException("Item not found or date must be before today.", McpErrorCode.InvalidParams);
    }

    [McpServerTool(Name = "daily_skip_for_date", Title = "Skip daily for date", OpenWorld = true)]
    [Description("Marks a past scheduled day as skipped. The skipped day bridges the streak. Returns the updated daily.")]
    public async Task<BoardItem> SkipForDate(
        [Description("Daily item id.")] Guid itemId,
        [Description("Date as yyyy-MM-dd.")] string date,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var day = McpInput.ParseDate(date);

        var result = await boards.SkipDailyForDateAsync(userId, itemId, day, null, cancellationToken);
        return result.Item
            ?? throw new McpProtocolException("Item not found or date must be before today.", McpErrorCode.InvalidParams);
    }

    [McpServerTool(Name = "update_daily", Title = "Update daily", Idempotent = true, OpenWorld = true)]
    [Description("Updates daily fields: title, notes, tags, start date, repeat schedule, streak counter, weekdays mask, checklist, sort order. Omit a field to keep its value. Pass an empty string to clear notes, tags, start date, or the checklist.")]
    public async Task<BoardItem> UpdateDaily(
        [Description("Daily item id.")] Guid itemId,
        [Description("Title, 1 to 200 chars, or null to keep.")] string? title = null,
        [Description("Notes up to 4000 chars, or null to keep. Empty string clears.")] string? notes = null,
        [Description("Comma-separated tags up to 500 chars, or null to keep. Empty string clears.")] string? tags = null,
        [Description("Start date as yyyy-MM-dd, or null to keep. Empty string clears.")] string? startDate = null,
        [Description("Repeat: Daily, Weekly, Monthly, or Yearly, or null to keep.")] DailyRepeatType? repeat = null,
        [Description("Repeat interval, 1 to 999, or null to keep.")] int? repeatInterval = null,
        [Description("Manual streak counter, 0 to 9999, or null to keep.")] int? counter = null,
        [Description("Weekday bitmask for weekly repeats, 0 means no mask, or null to keep.")] int? weekdays = null,
        [Description("Checklist JSON array, or null to keep. Empty string clears.")] string? checklistJson = null,
        [Description("Sort order position value, or null to keep. Prefer reorder_item to move items.")] double? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var current = await boards.GetItemAsync(userId, itemId, cancellationToken)
            ?? throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams);

        McpInput.CheckNotes(notes);
        McpInput.CheckTags(tags);
        var args = UpdateDailyArgs.From(current) with
        {
            Title = title is null ? current.Title : McpInput.CleanTitle(title),
            Notes = notes ?? current.Notes,
            Tags = tags ?? current.Tags,
            StartDate = startDate is null ? current.DailyStartDate : McpInput.ParseNullableDate(startDate, nameof(startDate)),
            Repeat = repeat ?? current.DailyRepeat,
            RepeatInterval = McpInput.ResolveRepeatInterval(repeatInterval, current.DailyRepeatInterval),
            ChecklistJson = checklistJson is null ? current.ChecklistJson : McpInput.NormalizedChecklist(EmptyToNull(checklistJson)),
            Counter = McpInput.ResolveDailyCounter(counter, current.Counter),
            SortOrder = sortOrder ?? current.SortOrder,
            Weekdays = McpInput.ResolveWeekdays(weekdays, current.DailyWeekdays),
        };
        var result = await boards.UpdateDailyAsync(userId, itemId, args, cancellationToken);
        return result.Status switch
        {
            BoardMutationStatus.Ok when result.Item is not null => result.Item,
            BoardMutationStatus.NotFound => throw new McpProtocolException("Item not found.", McpErrorCode.InvalidParams),
            _ => throw new McpProtocolException("Version conflict. Read the item again and retry.", McpErrorCode.InvalidRequest),
        };
    }

    private static string? EmptyToNull(string? value) => value is not null && value.Length == 0 ? null : value;
}
