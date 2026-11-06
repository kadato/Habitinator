using App.Shared.RCL.Models;
using App.Shared.RCL.Services.Board.Local;
using App.Shared.RCL.Services.Remote;

namespace App.Shared.RCL.Services;

/// <summary>
/// Routes statistics reads to this device while Data saver is on, while offline, or as a
/// fallback when the remote endpoint cannot be reached. Paused and offline reads
/// make no network calls and compute directly from the local mirror and activity log.
/// </summary>
public sealed class PauseAwareActivityStatisticsReader(
    RemoteActivityStatisticsReader remote,
    OfflineActivityStatisticsProvider offline,
    ILocalSettingsStore? store = null,
    IBoardSyncStatus? syncStatus = null) : IActivityStatisticsReader
{
    private bool IsPaused => BoardSyncPausePreference.IsPausedIn(store);

    private bool ShouldUseOfflineProvider =>
        IsPaused || (syncStatus?.IsOffline ?? false);

    private async Task<T> ReadWithFallbackAsync<T>(Func<Task<T>> remoteCall, Func<Task<T>> offlineCall)
    {
        if (ShouldUseOfflineProvider)
        {
            return await offlineCall().ConfigureAwait(false);
        }

        try
        {
            return await remoteCall().ConfigureAwait(false);
        }
        catch (Exception)
        {
            return await offlineCall().ConfigureAwait(false);
        }
    }

    public Task<ActivityDashboardDto> GetDashboardAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default) =>
        ReadWithFallbackAsync(
            () => remote.GetDashboardAsync(periodKey, tag, cancellationToken),
            () => offline.BuildDashboardAsync(periodKey, tag, cancellationToken));

    public Task<DailyContributionsViewDto> GetDailyContributionsAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default) =>
        ReadWithFallbackAsync(
            () => remote.GetDailyContributionsAsync(periodKey, tag, cancellationToken),
            () => offline.BuildDailyContributionsAsync(periodKey, tag, cancellationToken));

    public Task<HabitContributionsViewDto> GetHabitContributionsAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default) =>
        ReadWithFallbackAsync(
            () => remote.GetHabitContributionsAsync(periodKey, tag, cancellationToken),
            () => offline.BuildHabitContributionsAsync(periodKey, tag, cancellationToken));

    public Task<ActivityOverviewDto> GetOverviewAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default) =>
        ReadWithFallbackAsync(
            () => remote.GetOverviewAsync(periodKey, tag, cancellationToken),
            () => offline.BuildOverviewAsync(periodKey, tag, cancellationToken));

    public Task<ActivityDayDetailDto> GetActivityDayDetailAsync(DateOnly day, string? tag = null,
        CancellationToken cancellationToken = default) =>
        ReadWithFallbackAsync(
            () => remote.GetActivityDayDetailAsync(day, tag, cancellationToken),
            () => offline.BuildDayDetailAsync(day, tag, cancellationToken));

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
