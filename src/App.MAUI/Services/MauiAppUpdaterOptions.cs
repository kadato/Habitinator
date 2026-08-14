namespace App.MAUI.Services;

/// <summary>Where the update service checks for the latest release on GitHub.</summary>
public sealed class MauiAppUpdaterOptions
{
    public const string SectionName = "AppUpdater";

    /// <summary>GitHub API endpoint used to read the latest release.</summary>
    public Uri ReleasesApiUrl { get; set; } = new("https://api.github.com/repos/tothKarolyDavid/Habitinator/releases/latest");
}
