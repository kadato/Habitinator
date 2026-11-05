using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Board.Local;
using App.Shared.RCL.Services.Remote;

namespace App.Shared.RCL.Services;

/// <summary>
/// Routes statistics reads to this device while Data saver is on and to the
/// server otherwise. Paused reads make no network calls. The offline provider builds
/// the reads from the SQLite mirror and the local activity log. Mutations made while
/// paused keep both current. If you resume automatic sync or choose Sync now, the reader refreshes the server view.
/// </summary>
public sealed class PauseAwareActivityStatisticsReader(
    RemoteActivityStatisticsReader remote,
    OfflineActivityStatisticsProvider offline,
    ILocalSettingsStore? store = null) : IActivityStatisticsReader
{
    private bool IsPaused => BoardSyncPausePreference.IsPausedIn(store);

    public Task<ActivityDashboardDto> GetDashboardAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default) =>
        IsPaused
            ? offline.BuildDashboardAsync(periodKey, tag, cancellationToken)
            : remote.GetDashboardAsync(periodKey, tag, cancellationToken);

    public Task<DailyContributionsViewDto> GetDailyContributionsAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default) =>
        IsPaused
            ? offline.BuildDailyContributionsAsync(periodKey, tag, cancellationToken)
            : remote.GetDailyContributionsAsync(periodKey, tag, cancellationToken);

    public Task<HabitContributionsViewDto> GetHabitContributionsAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default) =>
        IsPaused
            ? offline.BuildHabitContributionsAsync(periodKey, tag, cancellationToken)
            : remote.GetHabitContributionsAsync(periodKey, tag, cancellationToken);

    public Task<ActivityOverviewDto> GetOverviewAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default) =>
        IsPaused
            ? offline.BuildOverviewAsync(periodKey, tag, cancellationToken)
            : remote.GetOverviewAsync(periodKey, tag, cancellationToken);

    public Task<ActivityDayDetailDto> GetActivityDayDetailAsync(DateOnly day, string? tag = null,
        CancellationToken cancellationToken = default) =>
        IsPaused
            ? offline.BuildDayDetailAsync(day, tag, cancellationToken)
            : remote.GetActivityDayDetailAsync(day, tag, cancellationToken);

    public bool TryGetCachedOverview(string? periodKey, string? tag, out ActivityOverviewDto? overview) =>
        remote.TryGetCachedOverview(periodKey, tag, out overview);

    public bool TryGetCachedDashboard(string? periodKey, string? tag, out ActivityDashboardDto? dashboard) =>
        remote.TryGetCachedDashboard(periodKey, tag, out dashboard);

    public bool TryGetCachedDailyContributions(string? periodKey, string? tag, out DailyContributionsViewDto? view) =>
        remote.TryGetCachedDailyContributions(periodKey, tag, out view);

    public bool TryGetCachedHabitContributions(string? periodKey, string? tag, out HabitContributionsViewDto? view) =>
        remote.TryGetCachedHabitContributions(periodKey, tag, out view);

    public void InvalidateCache()
    {
        remote.InvalidateCache();
        offline.InvalidateForTags(null);
    }

    public void InvalidateForTags(IEnumerable<string>? tags)
    {
        remote.InvalidateForTags(tags);
        offline.InvalidateForTags(tags);
    }

    public void InvalidateForItem(BoardItem item)
    {
        remote.InvalidateForItem(item);
        offline.InvalidateForTags(item.Tags is null ? null : [item.Tags]);
    }
}
