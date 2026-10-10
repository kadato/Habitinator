using System.ComponentModel;
using System.Globalization;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class TodoTools(BoardPersistenceService boards, IHttpContextAccessor http)
{
    [McpServerTool(Name = "update_todo", OpenWorld = true)]
    [Description("Updates todo fields: title, notes, tags, due date, checklist, sort order. Omit a field to keep its value. Pass an empty string to clear notes, tags, due date, or the checklist.")]
    public async Task<BoardItem?> UpdateTodo(
        [Description("Todo item id.")] Guid itemId,
        [Description("Title, 1 to 200 chars, or null to keep.")] string? title = null,
        [Description("Notes up to 4000 chars, or null to keep. Empty string clears.")] string? notes = null,
        [Description("Comma-separated tags up to 500 chars, or null to keep. Empty string clears.")] string? tags = null,
        [Description("Due date as yyyy-MM-dd, or null to keep. Empty string clears the due date.")] string? dueDate = null,
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
        var args = UpdateTodoArgs.From(current) with
        {
            Title = title is null ? current.Title : McpInput.CleanTitle(title),
            Notes = notes ?? current.Notes,
            Tags = tags ?? current.Tags,
            ChecklistJson = checklistJson is null ? current.ChecklistJson : McpInput.NormalizedChecklist(EmptyToNull(checklistJson)),
            DueDate = dueDate is null ? current.TodoDueDate : ParseNullableDate(dueDate),
            SortOrder = sortOrder ?? current.SortOrder,
        };
        var result = await boards.UpdateTodoAsync(userId, itemId, args, cancellationToken);
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
            throw new McpProtocolException("dueDate must use yyyy-MM-dd.", McpErrorCode.InvalidParams);
        }

        return parsed;
    }

    private static string? EmptyToNull(string? value) => value is not null && value.Length == 0 ? null : value;
}
