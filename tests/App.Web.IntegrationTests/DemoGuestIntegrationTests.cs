using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Remote;
using App.Web.Data;
using App.Web.Services;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class DemoGuestIntegrationTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = JsonDefaults.Api;

    [Fact]
    public async Task GuestJwtLogins_GetSeparateBoards()
    {
        var (clientA, loginA) = await CreateGuestClientAsync();
        var (clientB, loginB) = await CreateGuestClientAsync();

        loginA.Email.Should().NotBe(loginB.Email);

        var snapshotA = await clientA.GetFromJsonAsync<BoardSnapshot>("/api/board", s_json);
        var snapshotB = await clientB.GetFromJsonAsync<BoardSnapshot>("/api/board", s_json);
        snapshotA.Should().NotBeNull();
        snapshotB.Should().NotBeNull();
        snapshotA.Todos.Should().NotBeEmpty();
        snapshotB.Todos.Should().NotBeEmpty();

        var create = await clientA.PostAsJsonAsync(
            "/api/board/Todo",
            new ItemTitleRequest("Only for guest A"),
            s_json);
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<BoardItem>(s_json);
        created.Should().NotBeNull();

        var afterCreateB = await clientB.GetFromJsonAsync<BoardSnapshot>("/api/board", s_json);
        afterCreateB.Should().NotBeNull();
        afterCreateB.Todos.Should().NotContain(t => t.Id == created.Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clones = scope.ServiceProvider.GetRequiredService<DemoGuestCloneService>();
        var userAId = await db.Users.Where(u => u.Email == loginA.Email).Select(u => u.Id).SingleAsync();
        var userBId = await db.Users.Where(u => u.Email == loginB.Email).Select(u => u.Id).SingleAsync();
        (await clones.IsCloneAsync(userAId)).Should().BeTrue();
        (await clones.IsGuestAsync(userAId)).Should().BeTrue();
        (await db.BoardItems.CountAsync(x => x.UserId == userAId)).Should().BeGreaterThan(0);
        (await db.BoardItems.CountAsync(x => x.UserId == userBId)).Should().BeGreaterThan(0);

        var cloneBoardIds = await db.BoardItems
            .Where(x => x.UserId == userAId)
            .Select(x => x.Id)
            .ToListAsync();
        var eventBoardIds = await db.UserActivityEvents
            .Where(x => x.UserId == userAId && x.BoardItemId != null)
            .Select(x => x.BoardItemId!.Value)
            .Distinct()
            .ToListAsync();
        eventBoardIds.Should().NotBeEmpty();
        eventBoardIds.Should().OnlyContain(id => cloneBoardIds.Contains(id));
    }

    [Fact]
    public async Task GuestJwtClone_IsRemovedAfterItExpires()
    {
        var (_, login) = await CreateGuestClientAsync();

        Guid cloneId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            cloneId = await db.Users.Where(u => u.Email == login.Email).Select(u => u.Id).SingleAsync();
            await db.DemoGuestClones
                .Where(c => c.UserId == cloneId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAtUtc, DateTimeOffset.UtcNow.AddDays(-2)));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var clones = scope.ServiceProvider.GetRequiredService<DemoGuestCloneService>();
            (await clones.CleanupExpiredAsync()).Should().BeGreaterThan(0);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Users.AnyAsync(u => u.Id == cloneId)).Should().BeFalse();
            (await db.BoardItems.AnyAsync(x => x.UserId == cloneId)).Should().BeFalse();
            (await db.UserActivityEvents.AnyAsync(x => x.UserId == cloneId)).Should().BeFalse();
            (await db.DemoGuestClones.AnyAsync(c => c.UserId == cloneId)).Should().BeFalse();
            (await db.Users.AnyAsync(u => u.Email == "guest@habitinator.local")).Should().BeTrue();
        }
    }

    [Fact]
    public async Task BoardGet_DoesNotRefillAnEmptyClone()
    {
        var (client, login) = await CreateGuestClientAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userId = await db.Users.Where(u => u.Email == login.Email).Select(u => u.Id).SingleAsync();
            var now = DateTimeOffset.UtcNow;
            await db.BoardItems
                .Where(x => x.UserId == userId && x.DeletedAtUtc == null)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.DeletedAtUtc, now).SetProperty(x => x.UpdatedAtUtc, now));
        }

        var snapshot = await client.GetFromJsonAsync<BoardSnapshot>("/api/board", s_json);

        snapshot.Should().NotBeNull();
        (snapshot.Habits.Count + snapshot.Dailies.Count + snapshot.Todos.Count).Should().Be(0);
    }

    [Fact]
    public async Task CookieGuestLogin_Logout_RemovesTheCopy()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.PostAsync("/api/auth/guest-login", null);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location!.OriginalString.Should().Be("/");

        var status = await client.GetFromJsonAsync<AuthStatusDto>("/api/auth/status", s_json);
        status.Should().NotBeNull();
        status.IsAuthenticated.Should().BeTrue();
        status.Email.Should().NotBeNullOrWhiteSpace();
        status.Email.Should().NotBe("guest@habitinator.local");

        var logout = await client.PostAsync("/api/auth/cookie-logout", null);
        logout.StatusCode.Should().Be(HttpStatusCode.Redirect);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.AnyAsync(u => u.Email == status.Email)).Should().BeFalse();
    }

    [Fact]
    public async Task GuestChecks_DoNotReuseTheRequestDbContext()
    {
        var (_, login) = await CreateGuestClientAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var clones = scope.ServiceProvider.GetRequiredService<DemoGuestCloneService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await db.Users.Where(u => u.Email == login.Email).Select(u => u.Id).SingleAsync();

        // A guest check can run while the request pipeline still has work on the scoped
        // context. Keep that context busy and verify the check uses its own connection.
        var busy = db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(0.5)");
        (await clones.IsGuestAsync(userId)).Should().BeTrue();
        await busy;
    }

    private async Task<(HttpClient Client, LoginResponse Login)> CreateGuestClientAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsync("/api/auth/guest-jwt", null);
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>(s_json);
        payload.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);
        return (client, payload);
    }
}
