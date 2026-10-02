using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class UserDataImportIntegrationTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Export_Includes_Sections_Preferences_And_Local_Day()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);
        var dailyId = await CreateItemAsync(client, token, BoardSection.Daily, "Gym");
        await UpdateDailyAsync(client, token, dailyId);

        var export = await ExportAsync(client, token);

        export.Items.Should().ContainSingle(e => e.Item.Id == dailyId)
            .Which.Section.Should().Be(BoardSection.Daily);
        export.Items.Single(e => e.Item.Id == dailyId).Item.DailyWeekdays
            .Should().Be(DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday));
        export.Preferences.Should().NotBeNull();
        export.NotificationSettings.Should().NotBeNull();
        export.ExportedForLocalDay.Should().NotBe(default);
    }

    [Fact]
    public async Task Import_Replaces_Board_And_Restores_Events_And_Preferences()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);
        var habitId = await CreateItemAsync(client, token, BoardSection.Habit, "Read");
        await IncrementHabitAsync(client, token, habitId);
        var export = await ExportAsync(client, token);
        export.Events.Should().NotBeEmpty();

        var extraId = await CreateItemAsync(client, token, BoardSection.Todo, "Extra");
        var prefs = export.Preferences ?? UserPreferences.CreateDefault();
        prefs.DisplayName = "Importer";
        var changed = export with
        {
            Preferences = prefs
        };
        var result = await ImportAsync(client, token, JsonSerializer.Serialize(changed, s_json));

        result.Items.Should().Be(export.Items.Count);
        result.Events.Should().Be(export.Events.Count);
        result.PreferencesRestored.Should().BeTrue();

        var snapshot = await GetSnapshotAsync(client, token);
        var ids = snapshot.Habits.Select(h => h.Id)
            .Concat(snapshot.Dailies.Select(d => d.Id))
            .Concat(snapshot.Todos.Select(t => t.Id));
        ids.Should().BeEquivalentTo(export.Items.Select(e => e.Item.Id));
        ids.Should().NotContain(extraId);

        var reexport = await ExportAsync(client, token);
        reexport.Events.Should().HaveCount(export.Events.Count);
        reexport.Preferences!.DisplayName.Should().Be("Importer");
    }

    [Fact]
    public async Task Import_Same_File_Twice_Yields_Same_Board()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);
        await CreateItemAsync(client, token, BoardSection.Habit, "Stable");
        var export = await ExportAsync(client, token);
        var json = JsonSerializer.Serialize(export, s_json);

        await ImportAsync(client, token, json);
        var first = await GetSnapshotAsync(client, token);
        await ImportAsync(client, token, json);
        var second = await GetSnapshotAsync(client, token);

        second.Habits.Select(h => h.Id).Should().BeEquivalentTo(first.Habits.Select(h => h.Id));
        second.Dailies.Select(d => d.Id).Should().BeEquivalentTo(first.Dailies.Select(d => d.Id));
        second.Todos.Select(t => t.Id).Should().BeEquivalentTo(first.Todos.Select(t => t.Id));
    }

    [Fact]
    public async Task Import_Rejects_Legacy_Shapeless_Items()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);
        const string legacy = """{"exportedAtUtc":"2024-01-08T00:00:00Z","items":[{"title":"Old","isCompleted":false}],"events":[]}""";

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/account/import");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(legacy, Encoding.UTF8, "application/json");
        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_Rejects_Oversized_Files()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = await RegisterAndLoginAsync(client);
        var items = Enumerable.Range(0, 2001).Select(i =>
            new BoardSyncItem(BoardSection.Habit, new BoardItem(Guid.NewGuid(), $"Item {i}")));
        var payload = new UserDataExportDto(DateTimeOffset.UtcNow, [.. items], []);

        var res = await ImportRawAsync(client, token, JsonSerializer.Serialize(payload, s_json));

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> CreateItemAsync(HttpClient client, string token, BoardSection section, string title)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/board/{section}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new ItemTitleRequest(title), options: s_json);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<BoardItem>(s_json))!.Id;
    }

    private static async Task UpdateDailyAsync(HttpClient client, string token, Guid itemId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/board/dailies/{itemId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(
            new DailyUpdateRequest(
                "Gym", null, null, new DateOnly(2024, 1, 1),
                DailyRepeatType.Weekly, 1, null, 0, null,
                DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday)),
            options: s_json);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }

    private static async Task IncrementHabitAsync(HttpClient client, string token, Guid itemId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/board/habits/{itemId}/increment");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }

    private static async Task<UserDataExportDto> ExportAsync(HttpClient client, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/account/export");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<UserDataExportDto>(s_json))!;
    }

    private static async Task<UserDataImportResult> ImportAsync(HttpClient client, string token, string json)
    {
        var res = await ImportRawAsync(client, token, json);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<UserDataImportResult>(s_json))!;
    }

    private static async Task<HttpResponseMessage> ImportRawAsync(HttpClient client, string token, string json)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/account/import");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.SendAsync(req);
    }

    private static async Task<BoardSnapshot> GetSnapshotAsync(HttpClient client, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/board/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<BoardSnapshot>(s_json))!;
    }

    private static async Task<(string Token, string Email)> RegisterAndLoginAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"import-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var reg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        reg.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(s_json))!.AccessToken;
        return (token, email);
    }
}
