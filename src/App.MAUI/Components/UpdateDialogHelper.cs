using App.Shared.RCL.Components;
using App.Shared.RCL.Components.Dialogs;

using MudBlazor;

namespace App.MAUI.Components;

public static class UpdateDialogHelper
{
    public static Task ShowUpdateDialogAsync(
        IDialogService dialogService,
        string latestVersion,
        string releaseNotes,
        string downloadUrl)
    {
        var parameters = new DialogParameters<UpdateDialog>
        {
            { x => x.LatestVersion, latestVersion },
            { x => x.ReleaseNotes, releaseNotes },
            { x => x.DownloadUrl, downloadUrl }
        };

        return dialogService.ShowAsync<UpdateDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
    }
}
