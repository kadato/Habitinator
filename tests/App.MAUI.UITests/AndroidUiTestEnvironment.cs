using System.Diagnostics.CodeAnalysis;

namespace App.MAUI.UITests;

/// <summary>Android UI tests run only when opted in (emulator + Appium + APK). Default <c>dotnet test</c> skips them.</summary>
internal static class AndroidUiTestEnvironment
{
    internal const string EnableVar = "ANDROID_UI_TESTS";
    internal const string AppiumUrlVar = "APPIUM_SERVER_URL";
    internal const string AppPathVar = "ANDROID_APP_PATH";

    /// <summary>Default Appium 2 base URL.</summary>
    internal static string AppiumServerUrl =>
        Environment.GetEnvironmentVariable(AppiumUrlVar)?.Trim().TrimEnd('/')
        ?? "http://127.0.0.1:4723";

    internal static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnableVar), "1", StringComparison.Ordinal);

    internal static string? SkipReason { get; private set; }

    /// <summary>Resolve APK: explicit path, then newest *Signed*.apk under MAUI bin/Debug/net10.0-android.</summary>
    internal static bool TryGetApkPath([NotNullWhen(true)] out string? apkPath)
    {
        apkPath = null;
        SkipReason = null;

        if (!IsEnabled)
        {
            SkipReason =
                $"Set {EnableVar}=1, start an Android emulator, run Appium 2 (UiAutomator2), then re-run. Optional: {AppPathVar}, {AppiumUrlVar}.";
            return false;
        }

        var explicitPath = Environment.GetEnvironmentVariable(AppPathVar);
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            apkPath = Path.GetFullPath(explicitPath);
            return true;
        }

        var root = FindRepoRoot();
        if (root is null)
        {
            SkipReason = "Could not locate repo root (Habitinator.slnx).";
            return false;
        }

        var outDir = Path.Combine(root, "src", "App.MAUI", "bin", "Debug", "net10.0-android");
        if (!Directory.Exists(outDir))
        {
            SkipReason =
                $"Build the Android target first: dotnet build src/App.MAUI/App.MAUI.csproj -f net10.0-android -c Debug. Expected output folder: {outDir}";
            return false;
        }

        apkPath = Directory.GetFiles(outDir, "*-Signed.apk", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (apkPath is null)
        {
            apkPath = Directory.GetFiles(outDir, "*.apk", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        if (apkPath is null || !File.Exists(apkPath))
        {
            SkipReason = $"No APK found under {outDir}. Build the app for Android (Debug).";
            return false;
        }

        apkPath = Path.GetFullPath(apkPath);
        return true;
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Habitinator.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
