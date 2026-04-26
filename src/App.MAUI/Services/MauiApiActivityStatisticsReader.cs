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

    public async Task<ActivityDashboardDto> GetDashboardAsync(string? periodKey,
        CancellationToken cancellationToken = default)
    {
        var path = "api/activity/dashboard";
        if (!string.IsNullOrEmpty(periodKey)) path += $"?period={Uri.EscapeDataString(periodKey)}";

        return await GetJsonOrThrowAsync<ActivityDashboardDto>(path, cancellationToken);
    }

    public async Task<DailyContributionsViewDto> GetDailyContributionsAsync(string? periodKey,
        CancellationToken cancellationToken = default)
    {
        var path = "api/activity/daily-contributions";
        if (!string.IsNullOrEmpty(periodKey)) path += $"?period={Uri.EscapeDataString(periodKey)}";

        return await GetJsonOrThrowAsync<DailyContributionsViewDto>(path, cancellationToken);
    }

    public async Task<ActivityDayDetailDto> GetActivityDayDetailAsync(DateOnly day,
        CancellationToken cancellationToken = default)
    {
        var s = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var path = $"api/activity/day?date={Uri.EscapeDataString(s)}";
        return await GetJsonOrThrowAsync<ActivityDayDetailDto>(path, cancellationToken);
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
