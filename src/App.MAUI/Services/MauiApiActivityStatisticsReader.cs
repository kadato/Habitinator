using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using App.Shared.RCL.Services;

namespace App.MAUI.Services;

/// <summary>Reads the same server-backed statistics as the web app, via the authenticated API.</summary>
public sealed class MauiApiActivityStatisticsReader : IActivityStatisticsReader
{
    private static readonly JsonSerializerOptions Serializer = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _http;

    public MauiApiActivityStatisticsReader(IHttpClientFactory http)
    {
        _http = http;
    }

    private HttpClient Client => _http.CreateClient("api");

    public async Task<ActivityDashboardDto> GetDashboardAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var path = "api/activity/dashboard" + BuildActivityQuery(periodKey, tag);

        return await GetJsonOrThrowAsync<ActivityDashboardDto>(path, cancellationToken);
    }

    public async Task<DailyContributionsViewDto> GetDailyContributionsAsync(string? periodKey, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var path = "api/activity/daily-contributions" + BuildActivityQuery(periodKey, tag);

        return await GetJsonOrThrowAsync<DailyContributionsViewDto>(path, cancellationToken);
    }

    public async Task<ActivityDayDetailDto> GetActivityDayDetailAsync(DateOnly day, string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var s = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var path = "api/activity/day?date=" + Uri.EscapeDataString(s) +
                   (string.IsNullOrEmpty(tag) ? string.Empty : "&tag=" + Uri.EscapeDataString(tag));
        return await GetJsonOrThrowAsync<ActivityDayDetailDto>(path, cancellationToken);
    }

    private static string BuildActivityQuery(string? periodKey, string? tag)
    {
        var q = new List<string>();
        if (!string.IsNullOrEmpty(periodKey)) q.Add("period=" + Uri.EscapeDataString(periodKey));
        if (!string.IsNullOrEmpty(tag)) q.Add("tag=" + Uri.EscapeDataString(tag));
        return q.Count == 0 ? string.Empty : "?" + string.Join("&", q);
    }

    private async Task<T> GetJsonOrThrowAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        using var res = await Client.GetAsync(requestUri, cancellationToken);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Sign in required. Open Log in and try again.");

        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<T>(Serializer, cancellationToken);
        return body is null
            ? throw new InvalidOperationException("Empty response from the statistics API.")
            : body;
    }
}
