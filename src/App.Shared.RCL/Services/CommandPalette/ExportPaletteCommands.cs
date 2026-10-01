using App.Shared.RCL.Components;
using App.Shared.RCL.Components.Dialogs;

using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Services.CommandPalette;

/// <summary>Data export command.</summary>
internal sealed class ExportPaletteCommands(
    IUserDataExportService? export,
    IBoardDataService boardData,
    IJSRuntime? js,
    IDialogService dialogs,
    IUserNotifier notifier,
    Action close)
{
    private readonly IUserDataExportService? _export = export;
    private readonly IBoardDataService _boardData = boardData;
    private readonly IJSRuntime? _js = js;
    private readonly IDialogService _dialogs = dialogs;
    private readonly IUserNotifier _notifier = notifier;
    private readonly Action _close = close;

    public void AddExportCommand(List<CommandItem> list)
    {
        list.Add(new(
            Id: "action-export-data",
            Title: "Export data",
            Subtitle: "Download a backup of your board and activity data",
            Category: "Actions",
            Icon: Icons.Material.Filled.FileDownload,
            Action: ExportDataAsync,
            Keywords: ["export", "backup", "download", "json", "data"]));
    }

    public async Task ExportDataAsync()
    {
        _close();
        try
        {
            string json;
            DateTimeOffset exportedAt;
            if (_export is not null)
            {
                var data = await _export.ExportAsync();
                json = System.Text.Json.JsonSerializer.Serialize(data, JsonDefaults.Export);
                exportedAt = data.ExportedAtUtc;
            }
            else
            {
                var snapshot = await _boardData.GetSnapshotAsync();
                json = System.Text.Json.JsonSerializer.Serialize(snapshot, JsonDefaults.Export);
                exportedAt = DateTimeOffset.UtcNow;
            }

            var fileName = $"habitinator-export-{exportedAt:yyyyMMdd-HHmm}.json";
            var downloaded = false;
            if (_js is not null)
            {
                try
                {
                    await _js.InvokeVoidAsync("habitinatorLoadScript", "_content/App.Shared.RCL/js/boardUiState.js");
                    downloaded = await _js.InvokeAsync<bool>("habitinatorDownloadJson", fileName, json);
                }
                catch
                {
                    downloaded = false;
                }
            }

            if (downloaded)
            {
                await _notifier.NotifyAsync("Export downloaded.", Severity.Success);
            }
            else
            {
                await _dialogs.ShowAsync<ExportDataDialog>(
                    "Your data export",
                    new DialogParameters<ExportDataDialog> { { x => x.JsonText, json } },
                    DialogDefaults.Wide);
            }
        }
        catch (Exception ex)
        {
            await _notifier.NotifyAsync($"Export failed: {ex.Message}", Severity.Error);
        }
    }
}
