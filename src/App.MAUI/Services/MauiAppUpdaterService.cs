using System.Net.Http.Headers;
using System.Text.Json.Serialization;

using App.Shared.RCL.Services;

#if ANDROID
using Android.Content;
#endif


namespace App.MAUI.Services;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Update URL is static and pointing to public GitHub Releases API")]
public sealed partial class MauiAppUpdaterService : IAppUpdaterService, IDisposable
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
#if ANDROID || WINDOWS
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

            // Find platform-specific installer asset
#if ANDROID
            var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));
            var downloadUrl = asset?.BrowserDownloadUrl ?? string.Empty;
            var expectedAssetType = "Android APK (.apk)";
#elif WINDOWS
            var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) || a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            var downloadUrl = asset?.BrowserDownloadUrl ?? string.Empty;
            var expectedAssetType = "Windows Installer (.msi/.exe)";
#else
            var downloadUrl = string.Empty;
            var expectedAssetType = "supported platform installer";
#endif

            if (updateAvailable && string.IsNullOrEmpty(downloadUrl))
            {
                return new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    ErrorMessage = $"Update available but no {expectedAssetType} was found in the release."
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
#elif WINDOWS
        var uri = new Uri(downloadUrl);
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "update.msi";
        }
        var localPath = Path.Combine(FileSystem.CacheDirectory, fileName);
#endif

        // 1. Download file with progress report
#if ANDROID || WINDOWS
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
#endif

        // 2. Launch Installer
#if ANDROID
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
#elif WINDOWS
        if (!File.Exists(localPath))
        {
            throw new FileNotFoundException("Downloaded installer file not found.", localPath);
        }

        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = localPath,
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(processInfo);

        // Safely close the application windows to allow installer to overwrite
        if (Application.Current is not null)
        {
            var windows = Application.Current.Windows.ToArray();
            foreach (var window in windows)
            {
                try
                {
                    Application.Current.CloseWindow(window);
                }
                catch
                {
                    // Ignore exceptions during close
                }
            }
        }
        Environment.Exit(0);
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
