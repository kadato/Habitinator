using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class HabitTargetIntegrationTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = JsonDefaults.Api;

    [Fact]
    public async Task UpdateHabit_PersistsTargetCount_InSnapshot()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = await RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var create = await client.PostAsJsonAsync("/api/board/Habit", new ItemTitleRequest("Drink water"), s_json);
        create.EnsureSuccessStatusCode();
        var habit = await create.Content.ReadFromJsonAsync<BoardItem>(s_json)
            ?? throw new InvalidOperationException("Empty habit response.");

        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/board/habits/{habit.Id}");
        put.Content = JsonContent.Create(new HabitUpdateRequest(
            "Drink water", null, "health", true, false, HabitResetPeriod.Daily, 4, 0, null, null, 8), options: s_json);
        var update = await client.SendAsync(put);
        if (!update.IsSuccessStatusCode)
        {
            var body = await update.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Habit update failed with {(int)update.StatusCode}: {body}");
        }
        var updated = await update.Content.ReadFromJsonAsync<BoardItem>(s_json);
        updated!.TargetCount.Should().Be(8);

        var snapshot = await client.GetFromJsonAsync<BoardSnapshot>("/api/board", s_json);
        snapshot!.Habits.Single(h => h.Id == habit.Id).TargetCount.Should().Be(8);
    }

    [Fact]
    public async Task SkipDailyForDate_AllowsToday()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = await RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var create = await client.PostAsJsonAsync("/api/board/Daily", new ItemTitleRequest("Water"), s_json);
        create.EnsureSuccessStatusCode();
        var daily = await create.Content.ReadFromJsonAsync<BoardItem>(s_json)
            ?? throw new InvalidOperationException("Empty daily response.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var skip = new HttpRequestMessage(HttpMethod.Post, $"/api/board/dailies/{daily.Id}/skip-for-date");
        skip.Content = JsonContent.Create(new DailySkipForDateRequest(today), options: s_json);
        var response = await client.SendAsync(skip);

        response.EnsureSuccessStatusCode();
        var skipped = await response.Content.ReadFromJsonAsync<BoardItem>(s_json);
        skipped!.IsCompleted.Should().BeFalse();
        skipped.DailySkippedOn.Should().Be(today);
        DailySchedule.IsDueOnDate(skipped, today).Should().BeFalse();
    }

    private static async Task<string> RegisterAndLoginAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"target-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var reg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        reg.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>(s_json);
        return payload!.AccessToken;
    }
}
