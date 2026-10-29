using App.Shared.RCL.Models;
using App.Web.Services;

using ModelContextProtocol;

namespace App.Web.Mcp;

internal static class McpItemResolver
{
    public static async Task<Guid> ResolveItemIdAsync(
        BoardPersistenceService boards,
        Guid userId,
        Guid? itemId,
        string? title,
        BoardSection? section,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        if (itemId.HasValue && itemId.Value != Guid.Empty)
        {
            return itemId.Value;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new McpProtocolException("Specify either itemId or title.", McpErrorCode.InvalidParams);
        }

        var clean = title.Trim();
        var matches = await boards.SearchItemsAsync(userId, clean, null, section, 20, includeArchived, cancellationToken);
        if (matches.Count == 0)
        {
            var sectionDesc = section.HasValue ? $" in section '{section.Value}'" : "";
            throw new McpProtocolException($"No item found with title matching '{clean}'{sectionDesc}.", McpErrorCode.InvalidParams);
        }

        // 1. Exact title match (case-insensitive)
        var exact = matches.Where(m => string.Equals(m.Title, clean, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
        {
            return exact[0].Id;
        }
        if (exact.Count > 1)
        {
            var list = string.Join(", ", exact.Select(m => $"'{m.Title}' ({m.Id})"));
            throw new McpProtocolException($"Multiple items match exact title '{clean}': {list}. Specify itemId.", McpErrorCode.InvalidParams);
        }

        // 2. Starts-with match
        var startsWith = matches.Where(m => m.Title.StartsWith(clean, StringComparison.OrdinalIgnoreCase)).ToList();
        if (startsWith.Count == 1)
        {
            return startsWith[0].Id;
        }

        // 3. Single match overall
        if (matches.Count == 1)
        {
            return matches[0].Id;
        }

        // Ambiguous match
        var candidates = string.Join(", ", matches.Take(5).Select(m => $"'{m.Title}' ({m.Id})"));
        throw new McpProtocolException($"Ambiguous title '{clean}'. Matches: {candidates}. Specify itemId.", McpErrorCode.InvalidParams);
    }
}
