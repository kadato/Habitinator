using System.ComponentModel;
using System.Text.Json;

using App.Shared.RCL.Services;
using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class DataTools(
    UserDataExportService export,
    UserDataImportService import,
    IHttpContextAccessor http)
{
    [McpServerTool(Name = "export_data", Title = "Export user data", ReadOnly = true, OpenWorld = false)]
    [Description("Exports the full user board, activity events, and settings as a JSON document. The server refuses exports above 50000 rows. Feed the JSON back to import_data to restore.")]
    public async Task<string> ExportData(
        McpServer server,
        RequestContext<CallToolRequestParams> ctx,
        CancellationToken cancellationToken = default)
    {
        await McpProgress.ReportAsync(server, ctx, 0, 1, "Starting export.", cancellationToken);
        var dto = await export.BuildAsync(McpUser.RequireId(http), cancellationToken);
        await McpProgress.ReportAsync(server, ctx, 1, 1, "Export done.", cancellationToken);
        return JsonSerializer.Serialize(dto, App.Shared.RCL.Services.JsonDefaults.Export);
    }

    [McpServerTool(Name = "import_data", Title = "Import user data", Destructive = true, OpenWorld = true)]
    [Description("Restores a board from the JSON document that export_data returns. Items missing from the file become tombstones. Ask the user for confirmation first, then retry with confirmToken set to CONFIRM.")]
    public async Task<string> ImportData(
        McpServer server,
        RequestContext<CallToolRequestParams> ctx,
        [Description("Export JSON document as produced by export_data. Keep it under a few MB.")]
        string json,
        [Description("Confirmation token. Pass CONFIRM to confirm. Omit on the first call.")]
        string? confirmToken = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        if (!string.Equals(confirmToken, "CONFIRM", StringComparison.Ordinal)
            && !ConfirmImportAccepted(ctx))
        {
            if (server.IsMrtrSupported)
            {
                throw new InputRequiredException(new Dictionary<string, InputRequest>
                {
                    ["confirm_import"] = InputRequest.ForElicitation(new ElicitRequestParams
                    {
                        Message = "Import replaces the board. Confirm?",
                        RequestedSchema = new ElicitRequestParams.RequestSchema
                        {
                            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                            {
                                ["confirm"] = new ElicitRequestParams.BooleanSchema
                                {
                                    Description = "Confirm import",
                                    Default = false,
                                },
                            },
                            Required = ["confirm"],
                        },
                    }),
                }, requestState: JsonSerializer.Serialize(new { tool = "import_data" }));
            }

            throw new McpProtocolException(
                "Import replaces the board. Retry with confirmToken set to CONFIRM.",
                McpErrorCode.InvalidParams);
        }

        McpInput.CheckImportSize(json);
        await McpProgress.ReportAsync(server, ctx, 0, 1, "Starting import.", cancellationToken);
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
        await McpProgress.ReportAsync(server, ctx, 1, 1, "Import done.", cancellationToken);
        await McpNotifications.BoardChangedAsync(server, null, cancellationToken);
        return $"Imported {result.Items} items and {result.Events} events.";
    }

    private static bool ConfirmImportAccepted(RequestContext<CallToolRequestParams> ctx)
    {
        if (ctx.Params?.InputResponses is null
            || !ctx.Params.InputResponses.TryGetValue("confirm_import", out var resp))
        {
            return false;
        }

        if (resp is null)
        {
            return false;
        }

        var elicit = resp.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
        if (elicit is null || !elicit.IsAccepted || elicit.Content is null)
        {
            return false;
        }

        return elicit.Content.TryGetValue("confirm", out var value)
            && value.ValueKind == JsonValueKind.True;
    }
}
