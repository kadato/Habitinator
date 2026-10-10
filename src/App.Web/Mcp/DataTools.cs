using System.ComponentModel;
using System.Text.Json;

using App.Shared.RCL.Services;
using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class DataTools(
    UserDataExportService export,
    UserDataImportService import,
    IHttpContextAccessor http)
{
    [McpServerTool(Name = "export_data", ReadOnly = true, OpenWorld = false)]
    [Description("Exports the full user board, activity events, and settings as a JSON document. The server refuses exports above 50000 rows. Feed the JSON back to import_data to restore.")]
    public async Task<string> ExportData(CancellationToken cancellationToken = default)
    {
        var dto = await export.BuildAsync(McpUser.RequireId(http), cancellationToken);
        return JsonSerializer.Serialize(dto, App.Shared.RCL.Services.JsonDefaults.Export);
    }

    [McpServerTool(Name = "import_data", Destructive = true, OpenWorld = true)]
    [Description("Restores a board from the JSON document that export_data returns. Items missing from the file become tombstones. Ask the user for confirmation first, then retry with confirmToken set to CONFIRM.")]
    public async Task<string> ImportData(
        [Description("Export JSON document as produced by export_data. Keep it under a few MB.")]
        string json,
        [Description("Confirmation token. Pass CONFIRM to confirm. Omit on the first call.")]
        string? confirmToken = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (!string.Equals(confirmToken, "CONFIRM", StringComparison.Ordinal))
        {
            throw new McpProtocolException(
                "Import replaces the board. Retry with confirmToken set to CONFIRM.",
                McpErrorCode.InvalidParams);
        }

        UserDataExportDto? data;
        try
        {
            data = JsonSerializer.Deserialize<UserDataExportDto>(json, App.Shared.RCL.Services.JsonDefaults.Api);
        }
        catch (JsonException)
        {
            throw new McpProtocolException("The json is not a valid export document.", McpErrorCode.InvalidParams);
        }

        if (data is null)
        {
            throw new McpProtocolException("The json is not a valid export document.", McpErrorCode.InvalidParams);
        }

        var result = await import.ImportAsync(userId, data, cancellationToken);
        return $"Imported {result.Items} items and {result.Events} events.";
    }
}
