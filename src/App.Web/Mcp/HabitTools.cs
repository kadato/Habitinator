using System.ComponentModel;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class HabitTools(BoardPersistenceService boards, IHttpContextAccessor http)
{
    [McpServerTool(Name = "habit_plus", Title = "Add habit plus tick", OpenWorld = true)]
    [Description("Adds one plus tick to a habit. Resolves by itemId or title. Returns the updated habit. Throws when not found.")]
    public async Task<BoardItem> HabitPlus(
        [Description("Habit item id. Optional if title is provided.")] Guid? itemId = null,
        [Description("Habit title to match if itemId is omitted.")] string? title = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var resolvedId = await McpItemResolver.ResolveItemIdAsync(boards, userId, itemId, title, BoardSection.Habit, cancellationToken: cancellationToken);
        var result = await boards.IncrementHabitPlusAsync(userId, resolvedId, null, cancellationToken);
        return result.Item
            ?? throw new McpFieldException("itemId",
                $"no habit found with id {itemId:D}. Check it with get_snapshot or search_items.");
    }

    [McpServerTool(Name = "habit_minus", Title = "Add habit minus tick", OpenWorld = true)]
    [Description("Adds one minus tick to a habit. Resolves by itemId or title. Returns the updated habit. Throws when not found.")]
    public async Task<BoardItem> HabitMinus(
        [Description("Habit item id. Optional if title is provided.")] Guid? itemId = null,
        [Description("Habit title to match if itemId is omitted.")] string? title = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var resolvedId = await McpItemResolver.ResolveItemIdAsync(boards, userId, itemId, title, BoardSection.Habit, cancellationToken: cancellationToken);
        var result = await boards.IncrementHabitMinusAsync(userId, resolvedId, null, cancellationToken);
        return result.Item
            ?? throw new McpFieldException("itemId",
                $"no habit found with id {itemId:D}. Check it with get_snapshot or search_items.");
    }

    [McpServerTool(Name = "update_habit", Title = "Update habit", Idempotent = true, OpenWorld = true)]
    [Description("Updates habit fields: title, notes, tags, tracking flags, reset period, counters, checklist, sort order. Omit a field to keep its value. Pass an empty string to clear notes, tags, or the checklist.")]
    public async Task<BoardItem> UpdateHabit(
        [Description("Habit item id.")] Guid itemId,
        [Description("Title, 1 to 200 chars.")] string? title = null,
        [Description("Notes up to 4000 chars, or null to keep. Empty string clears.")] string? notes = null,
        [Description("Tags as comma-separated string or array of strings up to 500 chars, or null to keep. Empty string clears.")] string? tags = null,
        [Description("Track plus ticks, or null to keep.")] bool? trackPlus = null,
        [Description("Track minus ticks, or null to keep.")] bool? trackMinus = null,
        [Description("Reset period: Daily, Weekly, or Monthly.")] HabitResetPeriod? resetPeriod = null,
        [Description("Plus counter, zero or more, or null to keep.")] int? counter = null,
        [Description("Minus counter, zero or more, or null to keep.")] int? negativeCounter = null,
        [Description("Checklist as JSON string or native array of {text, isDone}, or null to keep. Empty string clears.")] string? checklistJson = null,
        [Description("Sort order position value, or null to keep. Prefer reorder_item to move items.")] double? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var current = await boards.GetItemAsync(userId, itemId, cancellationToken)
            ?? throw new McpFieldException("itemId",
                $"no habit found with id {itemId:D}. Check it with get_snapshot or search_items.");

        McpInput.CheckNotes(notes);
        var canonicalTags = tags is null ? null : McpInput.CanonicalTags(tags);
        McpInput.CheckTags(canonicalTags);
        var args = UpdateHabitArgs.From(current) with
        {
            Title = title is null ? current.Title : McpInput.CleanTitle(title),
            Notes = notes ?? current.Notes,
            Tags = canonicalTags ?? current.Tags,
            TrackPlus = trackPlus ?? current.TrackPlus,
            TrackMinus = trackMinus ?? current.TrackMinus,
            ResetPeriod = resetPeriod ?? current.ResetPeriod,
            Counter = McpInput.ResolveHabitCounter(counter, current.Counter, "Counter"),
            NegativeCounter = McpInput.ResolveHabitCounter(negativeCounter, current.NegativeCounter, "Negative counter"),
            ChecklistJson = checklistJson is null ? current.ChecklistJson : McpInput.NormalizedChecklist(EmptyToNull(checklistJson)),
            SortOrder = sortOrder ?? current.SortOrder,
        };
        var result = await boards.UpdateHabitAsync(userId, itemId, args, cancellationToken);
        return result.Status switch
        {
            BoardMutationStatus.Ok when result.Item is not null => result.Item,
            BoardMutationStatus.NotFound => throw new McpFieldException("itemId",
                $"no habit found with id {itemId:D}. Check it with get_snapshot or search_items."),
            _ => throw new McpProtocolException("Version conflict. Read the item again and retry.", McpErrorCode.InvalidRequest),
        };
    }

    private static string? EmptyToNull(string? value) => value is not null && value.Length == 0 ? null : value;
}
