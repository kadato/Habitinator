using System.Text.RegularExpressions;

namespace App.Shared.Tests;

/// <summary>
///     Guards the rule that <c>IUserDayService</c> is the only source of the local day.
///     UI components must not compute local dates directly. Service code may call the
///     raw schedule helpers only when no user-day service is available, such as in a
///     test double or an offline provider.
/// </summary>
public sealed partial class DayBoundaryGuardrailTests
{
    private static readonly string[] s_allowedDailyScheduleCallers =
    [
        "UserDayService.cs",
        "OfflineActivityStatisticsProvider.cs",
        "BoardPaletteCommands.cs",
        "NavigationPaletteCommands.cs",
        "CommandPaletteService.cs"
    ];

    [Fact]
    public void Components_DoNotComputeLocalTodayDirectly()
    {
        var components = Path.Combine(RepoRoot(), "src", "App.Shared.RCL", "Components");
        var offenders = FindMatches(components, LocalTodayCall())
            .Concat(FindMatches(components, UtcDayConversion()))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Components must use IUserDayService.Today instead of computing the local day. Offenders: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Services_UseTheSharedUserDayService()
    {
        var services = Path.Combine(RepoRoot(), "src", "App.Shared.RCL", "Services");
        var offenders = FindMatches(services, LocalTodayCall())
            .Where(path => !s_allowedDailyScheduleCallers.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Service day calculations must go through IUserDayService. Offenders: "
            + string.Join(", ", offenders));
    }

    private static IEnumerable<string> FindMatches(string root, Regex pattern)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".cs", StringComparison.Ordinal)
                                    || path.EndsWith(".razor", StringComparison.Ordinal)))
        {
            if (File.Exists(file) && pattern.IsMatch(File.ReadAllText(file)))
            {
                yield return Path.GetRelativePath(root, file);
            }
        }
    }

    [GeneratedRegex(@"DailySchedule\.LocalToday\s*\(")]
    private static partial Regex LocalTodayCall();

    [GeneratedRegex(@"DateOnly\.FromDateTime\(\s*DateTime(?:Offset)?\.UtcNow")]
    private static partial Regex UtcDayConversion();

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
