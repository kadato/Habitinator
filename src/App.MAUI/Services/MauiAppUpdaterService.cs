using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using App.Shared.RCL.Services;

#if ANDROID
using Android.Content;
using Android.OS;

using Microsoft.Maui.ApplicationModel;
#endif

namespace App.MAUI.Services;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Update URL is static and pointing to public GitHub Releases API")]
public sealed class MauiAppUpdaterService : IAppUpdaterService, IDisposable
{
    private readonly HttpClient _httpClient;

    public MauiAppUpdaterService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Habitinator", "1.0"));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    public bool IsSupported =>
#if ANDROID
        true;
#else
        false;
#endif

    public string CurrentVersion
    {
        get
        {
            try
            {
                return AppInfo.Current.VersionString;
            }
            catch
            {
                return "1.0.0";
            }
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        if (!IsSupported)
        {
            return new UpdateCheckResult { UpdateAvailable = false };
        }

        try
        {
            var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(
                "https://api.github.com/repos/tothKarolyDavid/Habitinator/releases/latest");

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    ErrorMessage = "Failed to parse release information from GitHub."
                };
            }

            var cleanTag = release.TagName.TrimStart('v');
            if (!Version.TryParse(cleanTag, out var latestVersion))
            {
                return new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    ErrorMessage = $"Failed to parse version tag: {release.TagName}"
                };
            }

            var currentVersion = AppInfo.Current.Version;
            var updateAvailable = latestVersion > currentVersion;

            // Find Android APK asset
            var apkAsset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));
            var downloadUrl = apkAsset?.BrowserDownloadUrl ?? string.Empty;

            if (updateAvailable && string.IsNullOrEmpty(downloadUrl))
            {
                return new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    ErrorMessage = "Update available but no Android APK asset was found in the release."
                };
            }

            return new UpdateCheckResult
            {
                UpdateAvailable = updateAvailable,
                LatestVersion = cleanTag,
                ReleaseNotes = release.Body,
                DownloadUrl = downloadUrl
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                UpdateAvailable = false,
                ErrorMessage = $"Error checking for updates: {ex.Message}"
            };
        }
    }

    public async Task DownloadAndInstallUpdateAsync(string downloadUrl, Action<double>? onProgress = null)
    {
        if (!IsSupported)
        {
            return;
        }

#if ANDROID
        var localPath = Path.Combine(FileSystem.CacheDirectory, "update.apk");

        // 1. Download APK with progress report
        using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            var totalReadBytes = 0L;
            int readBytes;

            while ((readBytes = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, readBytes));
                totalReadBytes += readBytes;

                if (totalBytes > 0)
                {
                    var progress = (double)totalReadBytes / totalBytes;
                    onProgress?.Invoke(progress);
                }
            }
        }

        // 2. Launch Package Installer on Android
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var file = new Java.IO.File(localPath);

        if (!file.Exists())
        {
            throw new FileNotFoundException("Downloaded APK file not found.", localPath);
        }

        // Request Install permission check for Android 8.0+ (Oreo / API 26)
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && context.PackageManager != null && !context.PackageManager.CanRequestPackageInstalls())
        {
            // Guide user to the system settings page to enable unknown sources install for this app
            var settingsIntent = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources);
            settingsIntent.SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"));
            settingsIntent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(settingsIntent);
            return; // Wait for user to enable it, they will have to tap install again
        }

        // Get file URI via FileProvider
        var apkUri = AndroidX.Core.Content.FileProvider.GetUriForFile(
            context,
            $"{context.PackageName}.fileprovider",
            file);

        var installIntent = new Intent(Intent.ActionView);
        installIntent.SetDataAndType(apkUri, "application/vnd.android.package-archive");
        installIntent.AddFlags(ActivityFlags.GrantReadUriPermission);
        installIntent.AddFlags(ActivityFlags.NewTask);

        context.StartActivity(installIntent);
#else
        await Task.CompletedTask;
#endif
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
