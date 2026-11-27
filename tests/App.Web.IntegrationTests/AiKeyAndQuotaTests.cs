using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Web.Data;
using App.Web.Services;

using FluentAssertions;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class AiKeyAndQuotaTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Protector_Encrypts_RoundTrips_And_Accepts_Legacy_Plaintext()
    {
        var protector = CreateProtector();

        var stored = protector.Protect("  sk-secret-123  ");

        stored.Should().StartWith("dp:v1:");
        stored.Should().NotContain("sk-secret-123");
        protector.Unprotect(stored).Should().Be("sk-secret-123");
        protector.Unprotect("legacy-plaintext-key").Should().Be("legacy-plaintext-key");
    }

    [Fact]
    public void PreferencesPrivacy_Merges_Writes_And_Masks_Reads()
    {
        var protector = CreateProtector();
        var existing = UserPreferences.CreateDefault();
        existing.Ai.ApiKey = protector.Protect("sk-old");

        var incoming = UserPreferences.CreateDefault();
        incoming.Ai.Model = "gpt-test";

        var kept = AiPreferencesPrivacy.ApplyWrite(incoming, existing, protector);
        protector.Unprotect(kept.Ai.ApiKey).Should().Be("sk-old");
        kept.Ai.ApiKeyConfigured.Should().BeTrue();

        var visible = AiPreferencesPrivacy.MaskForClient(kept);
        visible.Ai.ApiKey.Should().BeEmpty();
        visible.Ai.ApiKeyConfigured.Should().BeTrue();

        incoming = UserPreferences.CreateDefault();
        incoming.Ai.ApiKey = "sk-new";
        var replaced = AiPreferencesPrivacy.ApplyWrite(incoming, kept, protector);
        protector.Unprotect(replaced.Ai.ApiKey).Should().Be("sk-new");

        incoming = UserPreferences.CreateDefault();
        incoming.Ai.ClearApiKey = true;
        var cleared = AiPreferencesPrivacy.ApplyWrite(incoming, replaced, protector);
        cleared.Ai.ApiKey.Should().BeEmpty();
        cleared.Ai.ApiKeyConfigured.Should().BeFalse();
        cleared.Ai.ClearApiKey.Should().BeFalse();
    }

    [Fact]
    public async Task Settings_Endpoint_Never_Returns_The_Stored_Key_And_Preserves_It()
    {
        var (client, token, email) = await RegisterAsync();
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/settings/preferences");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResponse = await client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();
        var preferences = (await getResponse.Content.ReadFromJsonAsync<UserPreferences>(s_json))!;

        preferences.Ai.ApiKey = "sk-secret-route-key";
        using var putRequest = new HttpRequestMessage(HttpMethod.Put, "/api/settings/preferences")
        {
            Content = JsonContent.Create(preferences, options: s_json)
        };
        putRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(putRequest)).EnsureSuccessStatusCode();

        var stored = await ReadStoredPreferencesAsync(email);
        stored.Ai.ApiKey.Should().StartWith("dp:v1:");
        stored.Ai.ApiKey.Should().NotContain("sk-secret-route-key");

        using var readRequest = new HttpRequestMessage(HttpMethod.Get, "/api/settings/preferences");
        readRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var readResponse = await client.SendAsync(readRequest);
        var visible = (await readResponse.Content.ReadFromJsonAsync<UserPreferences>(s_json))!;
        visible.Ai.ApiKey.Should().BeEmpty();
        visible.Ai.ApiKeyConfigured.Should().BeTrue();

        // Saving an unrelated field or the masked model must not erase the key.
        visible.Ai.Model = "another-model";
        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, "/api/settings/preferences")
        {
            Content = JsonContent.Create(visible, options: s_json)
        };
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(updateRequest)).EnsureSuccessStatusCode();
        var afterUpdate = await ReadStoredPreferencesAsync(email);
        afterUpdate.Ai.ApiKey.Should().Be(stored.Ai.ApiKey);

        visible.Ai.ClearApiKey = true;
        using var clearRequest = new HttpRequestMessage(HttpMethod.Put, "/api/settings/preferences")
        {
            Content = JsonContent.Create(visible, options: s_json)
        };
        clearRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(clearRequest)).EnsureSuccessStatusCode();
        var cleared = await ReadStoredPreferencesAsync(email);
        cleared.Ai.ApiKey.Should().BeEmpty();
        cleared.Ai.ApiKeyConfigured.Should().BeFalse();
    }

    [Fact]
    public void ReadUsage_Handles_Both_Provider_Shapes()
    {
        var responses = System.Text.Json.Nodes.JsonNode.Parse(
            """{"usage":{"input_tokens":12,"output_tokens":34}}""");
        var completions = System.Text.Json.Nodes.JsonNode.Parse(
            """{"usage":{"prompt_tokens":5,"completion_tokens":6}}""");
        var missing = System.Text.Json.Nodes.JsonNode.Parse("""{"output":[]}""");

        AiQuotaService.ReadUsage(responses).Should().Be((12L, 34L));
        AiQuotaService.ReadUsage(completions).Should().Be((5L, 6L));
        AiQuotaService.ReadUsage(missing).Should().Be((0L, 0L));
        AiQuotaService.ReadUsage(System.Text.Json.Nodes.JsonNode.Parse("null")).Should().Be((0L, 0L));
    }

    [Fact]
    public async Task Quota_Denies_Requests_And_Tokens_After_The_Daily_Limit()
    {
        var user = await CreateUserAsync();
        var quota = CreateQuota(new AiQuotaOptions
        {
            AllowSharedOperatorKey = true,
            SharedKeyDailyRequests = 2,
            SharedKeyDailyTokens = 100,
            GuestSharedKeyDailyRequests = 1,
            GuestSharedKeyDailyTokens = 10
        });

        (await quota.TryBeginRequestAsync(user.Id, isGuest: false)).Should().BeNull();
        (await quota.TryBeginRequestAsync(user.Id, isGuest: false)).Should().BeNull();
        (await quota.TryBeginRequestAsync(user.Id, isGuest: false)).Should().Be(AiQuotaService.LimitMessage);

        var tokenUser = await CreateUserAsync();
        await quota.RecordUsageAsync(tokenUser.Id, inputTokens: 80, outputTokens: 30);
        (await quota.TryBeginRequestAsync(tokenUser.Id, isGuest: false)).Should().Be(AiQuotaService.LimitMessage);
    }

    [Fact]
    public async Task Quota_Uses_The_Tighter_Guest_Limit()
    {
        var user = await CreateUserAsync();
        var quota = CreateQuota(new AiQuotaOptions
        {
            AllowSharedOperatorKey = true,
            SharedKeyDailyRequests = 50,
            SharedKeyDailyTokens = 100_000,
            GuestSharedKeyDailyRequests = 1,
            GuestSharedKeyDailyTokens = 10_000
        });

        (await quota.TryBeginRequestAsync(user.Id, isGuest: true)).Should().BeNull();
        (await quota.TryBeginRequestAsync(user.Id, isGuest: true)).Should().Be(AiQuotaService.LimitMessage);
    }

    [Fact]
    public async Task Demo_Guest_Cannot_Spend_The_Shared_Operator_Key()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiAssistantService>();
        var guest = await userManager.FindByEmailAsync("guest@habitinator.local");
        guest.Should().NotBeNull();

        var result = await aiService.ChatAsync(
            guest.Id,
            new AiPreferences(),
            new AiChatRequest { Messages = [AiChatMessage.User("Hello")] },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not available in the demo");

        var test = await aiService.TestConnectionAsync(
            new AiTestConnectionRequest
            {
                Endpoint = AiPreferences.DefaultOpenCodeGoEndpoint,
                Model = AiPreferences.DefaultModel
            },
            guest.Id,
            CancellationToken.None);
        test.Success.Should().BeFalse();
        test.Message.Should().Contain("not available in the demo");
    }

    [Fact]
    public async Task Startup_Migration_Encrypts_Legacy_Plaintext_Keys()
    {
        var user = await CreateUserAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.Users.FirstAsync(u => u.Id == user.Id);
            var prefs = UserPreferences.CreateDefault();
            prefs.Ai.ApiKey = "sk-legacy-plaintext";
            row.UserPreferences = prefs;
            await db.SaveChangesAsync();
        }

        var changed = await AiApiKeyEncryptionHostedService.EncryptExistingKeysAsync(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            CancellationToken.None);
        changed.Should().BeGreaterThanOrEqualTo(0);

        var stored = await ReadStoredPreferencesAsync(user.Email!);
        stored.Ai.ApiKey.Should().StartWith("dp:v1:");
        stored.Ai.ApiKey.Should().NotContain("sk-legacy-plaintext");
    }

    private AiQuotaService CreateQuota(AiQuotaOptions options)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        return new AiQuotaService(
            dbFactory,
            Options.Create(options),
            NullLogger<AiQuotaService>.Instance);
    }

    private static AiApiKeyProtector CreateProtector() =>
        new(new EphemeralDataProtectionProvider(), NullLogger<AiApiKeyProtector>.Instance);

    private async Task<ApplicationUser> CreateUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser
        {
            UserName = $"quota-{suffix}@integration.test",
            Email = $"quota-{suffix}@integration.test"
        };
        (await userManager.CreateAsync(user, "TestUser1!Aa")).Succeeded.Should().BeTrue();
        return user;
    }

    private async Task<(HttpClient Client, string Token, string Email)> RegisterAsync()
    {
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"prefs-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password)))
            .EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password, RememberMe: false));
        login.EnsureSuccessStatusCode();
        var data = (await login.Content.ReadFromJsonAsync<LoginResponse>(s_json))!;
        return (client, data.AccessToken, email);
    }

    private async Task<UserPreferences> ReadStoredPreferencesAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var preferences = await db.Users.AsNoTracking()
            .Where(u => u.Email == email)
            .Select(u => u.UserPreferences)
            .SingleAsync();
        preferences.Should().NotBeNull();
        return preferences;
    }
}
