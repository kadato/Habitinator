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

        var options = new DialogOptions
        {
            CloseButton = true,
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.Small,
            FullWidth = true
        };

        return dialogService.ShowAsync<UpdateDialog>("New Update Available", parameters, options);
    }
}
