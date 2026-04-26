using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using Microsoft.Maui.Storage;

namespace App.MAUI.Services;

/// <summary>
/// When logged in, loads and saves notification preferences on the server (same row as web).
/// Falls back to <see cref="Preferences"/> when offline or not authenticated.
/// </summary>
public sealed class MauiApiNotificationSettingsService : INotificationSettingsService
{
    private const string PreferencesKey = "notification_settings_v1";

    private static readonly JsonSerializerOptions Serializer = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _http;
    private readonly IApiSession _apiSession;

    public MauiApiNotificationSettingsService(IHttpClientFactory http, IApiSession apiSession)
    {
        _http = http;
        _apiSession = apiSession;
        _apiSession.Changed += (_, _) => Changed?.Invoke();
    }

    public event Action? Changed;

    private HttpClient Client => _http.CreateClient("api");

    public async Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSessionReadyAsync(cancellationToken).ConfigureAwait(false);
        if (!_apiSession.IsLoggedIn)
        {
            return ReadLocal();
        }

        try
        {
            using HttpResponseMessage res = await Client.GetAsync("api/settings/notifications", cancellationToken).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                return ReadLocal();
            }

            NotificationSettings? remote = await res.Content
                .ReadFromJsonAsync<NotificationSettings>(Serializer, cancellationToken)
                .ConfigureAwait(false);
            if (remote is null)
            {
                return ReadLocal();
            }

            WriteLocal(remote);
            return remote;
        }
        catch (HttpRequestException)
        {
            return ReadLocal();
        }
    }

    public async Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
    {
        await EnsureSessionReadyAsync(cancellationToken).ConfigureAwait(false);
        WriteLocal(settings);

        if (!_apiSession.IsLoggedIn)
        {
            Changed?.Invoke();
            return;
        }

        using HttpResponseMessage res = await Client
            .PutAsJsonAsync("api/settings/notifications", settings, Serializer, cancellationToken)
            .ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        Changed?.Invoke();
    }

    private async Task EnsureSessionReadyAsync(CancellationToken cancellationToken)
    {
        if (!_apiSession.IsReady)
        {
            await _apiSession.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private NotificationSettings ReadLocal()
    {
        string? json = Preferences.Get(PreferencesKey, null);
        return NotificationSettingsJson.DeserializeOrDefault(json);
    }

    private static void WriteLocal(NotificationSettings settings) =>
        Preferences.Set(PreferencesKey, NotificationSettingsJson.Serialize(settings));
}
