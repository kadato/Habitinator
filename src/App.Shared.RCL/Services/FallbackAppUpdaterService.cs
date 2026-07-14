namespace App.Shared.RCL.Services;

public class FallbackAppUpdaterService : IAppUpdaterService
{
    public bool IsSupported => false;
    public string CurrentVersion => "1.0.0";

    public Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        return Task.FromResult(new UpdateCheckResult { UpdateAvailable = false });
    }

    public Task DownloadAndInstallUpdateAsync(string downloadUrl, Action<double>? onProgress = null)
    {
        return Task.CompletedTask;
    }
}
