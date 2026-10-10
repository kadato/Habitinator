using System.ComponentModel;
using System.Globalization;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class DailyTools(BoardPersistenceService boards, IHttpContextAccessor http)
{
    [McpServerTool(Name = "daily_complete_for_date", OpenWorld = true)]
    [Description("Marks a daily complete for a calendar date in yyyy-MM-dd form. Returns the updated daily.")]
    public async Task<BoardItem?> CompleteForDate(
        [Description("Daily item id.")] Guid itemId,
        [Description("Date as yyyy-MM-dd.")] string date,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (!DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new McpProtocolException("Date must use yyyy-MM-dd.", McpErrorCode.InvalidParams);
        }

        var result = await boards.CompleteDailyForDateAsync(userId, itemId, day, null, cancellationToken);
        return result.Item;
    }

    [McpServerTool(Name = "daily_skip_for_date", OpenWorld = true)]
    [Description("Marks a past scheduled day as skipped. The skipped day bridges the streak. Returns the updated daily.")]
    public async Task<BoardItem?> SkipForDate(
        [Description("Daily item id.")] Guid itemId,
        [Description("Date as yyyy-MM-dd.")] string date,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (!DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new McpProtocolException("Date must use yyyy-MM-dd.", McpErrorCode.InvalidParams);
        }

        var result = await boards.SkipDailyForDateAsync(userId, itemId, day, null, cancellationToken);
        return result.Item;
    }

    [McpServerTool(Name = "update_daily", OpenWorld = true)]
    [Description("Updates daily fields: title, notes, tags, start date, repeat schedule, streak counter, weekdays mask, checklist, sort order. Omit a field to keep its value. Pass an empty string to clear notes, tags, start date, or the checklist.")]
    public async Task<BoardItem?> UpdateDaily(
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
        var current = await boards.GetItemAsync(userId, itemId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        McpInput.CheckNotes(notes);
        McpInput.CheckTags(tags);
        var args = UpdateDailyArgs.From(current) with
        {
            Title = title is null ? current.Title : McpInput.CleanTitle(title),
            Notes = notes ?? current.Notes,
            Tags = tags ?? current.Tags,
            StartDate = startDate is null ? current.DailyStartDate : ParseNullableDate(startDate),
            Repeat = repeat ?? current.DailyRepeat,
            RepeatInterval = repeatInterval.HasValue ? Math.Clamp(repeatInterval.Value, 1, 999) : current.DailyRepeatInterval,
            ChecklistJson = checklistJson is null ? current.ChecklistJson : McpInput.NormalizedChecklist(EmptyToNull(checklistJson)),
            Counter = counter.HasValue ? Math.Clamp(counter.Value, 0, 9999) : current.Counter,
            SortOrder = sortOrder ?? current.SortOrder,
            Weekdays = weekdays ?? current.DailyWeekdays,
        };
        var result = await boards.UpdateDailyAsync(userId, itemId, args, cancellationToken);
        return result.Item;
    }

    private static DateOnly? ParseNullableDate(string raw)
    {
        if (raw.Length == 0)
        {
            return null;
        }

        if (!DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new McpProtocolException("Date must use yyyy-MM-dd.", McpErrorCode.InvalidParams);
        }

        return parsed;
    }

    private static string? EmptyToNull(string? value) => value is not null && value.Length == 0 ? null : value;
}
