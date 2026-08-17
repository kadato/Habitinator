using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteActivityStatisticsReader : IActivityStatisticsReader
{
    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;

    private readonly IHttpClientFactory _http;

    public RemoteActivityStatisticsReader(IHttpClientFactory http)
    {
        _http = http;
    }

    private HttpClient Client => _http.CreateClient("api");

    private readonly ConcurrentDictionary<string, (object Value, DateTime ExpiresAtUtc)> _cache = new();
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromSeconds(60);

    public async Task<ActivityOverviewDto> GetOverviewAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var path = "api/activity/overview" + BuildActivityQuery(periodKey, tag);
        return await GetJsonCachedOrThrowAsync<ActivityOverviewDto>(path, DefaultCacheTtl, cancellationToken);
    }

    public async Task<ActivityDashboardDto> GetDashboardAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var path = "api/activity/dashboard" + BuildActivityQuery(periodKey, tag);

        return await GetJsonCachedOrThrowAsync<ActivityDashboardDto>(path, DefaultCacheTtl, cancellationToken);
    }

    public async Task<DailyContributionsViewDto> GetDailyContributionsAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var path = "api/activity/daily-contributions" + BuildActivityQuery(periodKey, tag);

        return await GetJsonCachedOrThrowAsync<DailyContributionsViewDto>(path, DefaultCacheTtl, cancellationToken);
    }

    public async Task<HabitContributionsViewDto> GetHabitContributionsAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var path = "api/activity/habit-contributions" + BuildActivityQuery(periodKey, tag);

        return await GetJsonCachedOrThrowAsync<HabitContributionsViewDto>(path, DefaultCacheTtl, cancellationToken);
    }

    public async Task<ActivityDayDetailDto> GetActivityDayDetailAsync(DateOnly day, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var s = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var path = "api/activity/day?date=" + Uri.EscapeDataString(s) +
                   (string.IsNullOrEmpty(tag) ? string.Empty : "&tag=" + Uri.EscapeDataString(tag));
        return await GetJsonCachedOrThrowAsync<ActivityDayDetailDto>(path, DefaultCacheTtl, cancellationToken);
    }

    public void InvalidateCache()
    {
        _cache.Clear();
    }

    private static string BuildActivityQuery(string? periodKey, string? tag)
    {
        var q = new List<string>();
        if (!string.IsNullOrEmpty(periodKey))
        {
            q.Add("period=" + Uri.EscapeDataString(periodKey));
        }

        if (!string.IsNullOrEmpty(tag))
        {
            q.Add("tag=" + Uri.EscapeDataString(tag));
        }

        return q.Count == 0 ? string.Empty : "?" + string.Join("&", q);
    }

    private async Task<T> GetJsonCachedOrThrowAsync<T>(string requestUri, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(requestUri, out var entry) && entry.ExpiresAtUtc > now && entry.Value is T cachedValue)
        {
            return cachedValue;
        }

        var result = await GetJsonOrThrowAsync<T>(requestUri, cancellationToken);
        _cache[requestUri] = (result!, now.Add(ttl));
        return result;
    }

    private async Task<T> GetJsonOrThrowAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        using var res = await Client.GetAsync(requestUri, cancellationToken);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Sign in required. Open Log in and try again.");
        }

        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<T>(Serializer, cancellationToken);
        return body is null
            ? throw new InvalidOperationException("Empty response from the statistics API.")
            : body;
    }
}
