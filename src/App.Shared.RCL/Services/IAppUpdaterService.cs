namespace App.Shared.RCL.Services;

public interface IAppUpdaterService
{
    bool IsSupported { get; }
    string CurrentVersion { get; }
    Task<UpdateCheckResult> CheckForUpdateAsync();
    Task DownloadAndInstallUpdateAsync(string downloadUrl, Action<double>? onProgress = null);
}

public class UpdateCheckResult
{
    public bool UpdateAvailable { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
