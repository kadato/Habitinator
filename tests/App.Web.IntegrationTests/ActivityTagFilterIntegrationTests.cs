using System.Net.Http.Headers;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class ActivityTagFilterIntegrationTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Overview_tag_filter_matches_case_insensitively()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = await RegisterAndLoginAsync(client);

        var daily = await CreateDailyAsync(client, token, "Run");
        await TagDailyAsync(client, token, daily.Id, "Health");

        using var complete = new HttpRequestMessage(HttpMethod.Post, $"/api/board/Daily/{daily.Id}/toggle");
        complete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(complete)).EnsureSuccessStatusCode();

        using var unfiltered = new HttpRequestMessage(HttpMethod.Get, "/api/activity/overview");
        unfiltered.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var unfilteredRes = await client.SendAsync(unfiltered);
        unfilteredRes.EnsureSuccessStatusCode();
        var unfilteredBody = await unfilteredRes.Content.ReadFromJsonAsync<ActivityOverviewDto>(s_json) ?? throw new InvalidOperationException("Empty overview response.");
        unfilteredBody.Dashboard.AvailableTags.Should().ContainSingle(t => t.Equals("Health", StringComparison.OrdinalIgnoreCase));

        using var filtered = new HttpRequestMessage(HttpMethod.Get, "/api/activity/overview?tag=HEALTH");
        filtered.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var filteredRes = await client.SendAsync(filtered);
        filteredRes.EnsureSuccessStatusCode();
        var filteredBody = await filteredRes.Content.ReadFromJsonAsync<ActivityOverviewDto>(s_json) ?? throw new InvalidOperationException("Empty overview response.");
        filteredBody.Dashboard.TotalEvents.Should().BeGreaterThanOrEqualTo(1);
    }

    private static async Task<BoardItem> CreateDailyAsync(HttpClient client, string token, string title)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/board/Daily");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new ItemTitleRequest(title), options: s_json);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<BoardItem>(s_json))!;
    }

    private static async Task TagDailyAsync(HttpClient client, string token, Guid id, string tags)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/board/dailies/{id}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(
            new DailyUpdateRequest("Run", null, tags, null, DailyRepeatType.Daily, 1, null),
            options: s_json);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }

    private static async Task<string> RegisterAndLoginAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"tag-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var reg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        reg.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>(s_json))!.AccessToken;
    }
}
