using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

#pragma warning disable S1075 // MCP resource URIs are protocol identifiers, not file paths.
[McpServerResourceType]
public sealed class BoardResources(
    BoardPersistenceService boards,
    ActivityStatisticsService stats,
    IHttpContextAccessor http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [McpServerResource(UriTemplate = "board://snapshot", Name = "Board snapshot", Title = "Board snapshot", MimeType = "application/json")]
    [Description("Current board mirror, first 200 rows by sort order. Use get_snapshot for paging or search_items to find one item.")]
    public async Task<TextResourceContents> GetSnapshot(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);
        return new TextResourceContents
        {
            Uri = "board://snapshot",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(snapshot, Json),
        };
    }

    [McpServerResource(UriTemplate = "board://archived", Name = "Archived board", Title = "Archived board", MimeType = "application/json")]
    [Description("Archived items hidden from the active board, first 200 rows.")]
    public async Task<TextResourceContents> GetArchived(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var snapshot = await boards.GetArchivedSnapshotAsync(userId, 200, cancellationToken);
        return new TextResourceContents
        {
            Uri = "board://archived",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(snapshot, Json),
        };
    }

    [McpServerResource(UriTemplate = "board://item/{id}", Name = "Board item", Title = "Board item", MimeType = "application/json")]
    [Description("One board item by id.")]
    public async Task<TextResourceContents> GetItem(
        [Description("Board item id.")] string id,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (!Guid.TryParse(id, out var itemId))
        {
            throw new McpProtocolException($"Invalid item id: {id}", McpErrorCode.InvalidParams);
        }

        var item = await boards.GetItemAsync(userId, itemId, cancellationToken)
            ?? throw new McpProtocolException($"Item not found: {id}", McpErrorCode.InvalidParams);

        return new TextResourceContents
        {
            Uri = $"board://item/{id}",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(item, Json),
        };
    }

    [McpServerResource(UriTemplate = "activity://dashboard", Name = "Activity dashboard", Title = "Activity dashboard", MimeType = "application/json")]
    [Description("Activity dashboard heatmap data with default period.")]
    public async Task<TextResourceContents> GetDashboard(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var dashboard = await stats.GetDashboardAsync(userId, null, null, cancellationToken);
        return new TextResourceContents
        {
            Uri = "activity://dashboard",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(dashboard, Json),
        };
    }

    [McpServerResource(UriTemplate = "activity://day/{date}", Name = "Activity day", Title = "Activity day", MimeType = "application/json")]
    [Description("Activity detail for one date in yyyy-MM-dd form.")]
    public async Task<TextResourceContents> GetDay(
        [Description("Date as yyyy-MM-dd.")] string date,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (!DateOnly.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new McpProtocolException($"Invalid date: {date}. Use yyyy-MM-dd.", McpErrorCode.InvalidParams);
        }

        var detail = await stats.GetActivityDayDetailAsync(userId, day, null, cancellationToken);
        return new TextResourceContents
        {
            Uri = $"activity://day/{date}",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(detail, Json),
        };
    }
}
