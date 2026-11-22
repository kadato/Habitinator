using FluentAssertions;

namespace App.Shared.Tests;

/// <summary>
///     Keeps the two client hosts on one composition path. If a shared service is
///     registered directly in a host, the hosts can drift and one client silently
///     loses a feature.
/// </summary>
public sealed class ClientCompositionGuardrailTests
{
    private static readonly string[] s_sharedRegistrations =
    [
        "IUserDayService",
        "GlobalTimerService",
        "RemoteBoardRefreshService",
        "BoardRemoteNotifyBridge",
        "RemoteBoardDataService",
        "BoardSyncStatus",
        "BoardSyncPausePreference",
        "BoardInitialLoadSignal",
        "LocalFirstBoardDataService",
        "BoardSyncCoordinator",
        "IBoardSyncRequestor",
        "IUserTimeZoneService",
        "IUserPreferencesService",
        "IActivityStatisticsReader",
        "RemoteActivityStatisticsReader",
        "IUserActivityLogService",
        "INotificationSettingsService",
        "IUserDataExportService",
        "IUserDataImportService",
        "IUserNotifier",
        "ICommandPaletteService"
    ];

    [Fact]
    public void BothClients_UseTheSharedClientRegistration()
    {
        foreach (var host in HostFiles())
        {
            File.ReadAllText(host).Should().Contain("AddHabitinatorClientCore(");
        }
    }

    [Fact]
    public void Hosts_DoNotRegisterSharedServicesDirectly()
    {
        foreach (var host in HostFiles())
        {
            var text = File.ReadAllText(host);
            var offenders = s_sharedRegistrations
                .Where(service => text.Contains($"AddSingleton<{service}", StringComparison.Ordinal)
                                  || text.Contains($"AddScoped<{service}", StringComparison.Ordinal))
                .ToList();

            offenders.Should().BeEmpty($"{Path.GetFileName(host)} must register shared services through AddHabitinatorClientCore.");
        }
    }

    private static IEnumerable<string> HostFiles()
    {
        var root = RepoRoot();
        yield return Path.Combine(root, "src", "App.Web.Client", "Program.cs");
        yield return Path.Combine(root, "src", "App.MAUI", "MauiProgram.cs");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Habitinator.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
