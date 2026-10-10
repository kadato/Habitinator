using App.Shared.RCL.Models;

using ModelContextProtocol;

namespace App.Web.Mcp;

internal static class McpInput
{
    public const int MaxTitleLength = 200;
    public const int MaxNotesLength = 4000;
    public const int MaxTagsLength = 500;
    public const int MaxChecklistLength = 8000;

    public static string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitleLength)
        {
            throw new McpProtocolException("Title must be 1 to 200 chars.", McpErrorCode.InvalidParams);
        }

        return title.Trim();
    }

    public static void CheckNotes(string? notes)
    {
        if (notes is not null && notes.Length > MaxNotesLength)
        {
            throw new McpProtocolException("Notes must be 4000 chars or fewer.", McpErrorCode.InvalidParams);
        }
    }

    public static void CheckTags(string? tags)
    {
        if (tags is not null && tags.Length > MaxTagsLength)
        {
            throw new McpProtocolException("Tags must be 500 chars or fewer.", McpErrorCode.InvalidParams);
        }
    }

    public static string? NormalizedChecklist(string? checklistJson)
    {
        var normalized = DailyChecklistJson.Normalize(checklistJson);
        if (normalized is not null && normalized.Length > MaxChecklistLength)
        {
            throw new McpProtocolException("Checklist must be 8000 chars or fewer.", McpErrorCode.InvalidParams);
        }

        return normalized;
    }

    public static string CleanChecklistText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 500)
        {
            throw new McpProtocolException("Checklist text must be 1 to 500 chars.", McpErrorCode.InvalidParams);
        }

        return text.Trim();
    }
}
