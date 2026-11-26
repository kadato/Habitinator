using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class DemoGuestIntegrationTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = JsonDefaults.Api;

    [Fact]
    public async Task BoardGet_ReseedsDemoGuest_WhenSnapshotIsEmpty()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsync("/api/auth/guest-jwt", null);
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>(s_json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var guest = await users.FindByEmailAsync("guest@habitinator.local");
            guest.Should().NotBeNull();

            var now = DateTimeOffset.UtcNow;
            await db.BoardItems
                .Where(x => x.UserId == guest.Id && x.DeletedAtUtc == null)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.DeletedAtUtc, now).SetProperty(x => x.UpdatedAtUtc, now));
        }

        var snapshot = await client.GetFromJsonAsync<BoardSnapshot>("/api/board", s_json);

        snapshot.Should().NotBeNull();
        (snapshot.Habits.Count + snapshot.Dailies.Count + snapshot.Todos.Count).Should().BeGreaterThan(0);
    }
}
