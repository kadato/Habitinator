using System.Net;
using System.Text.Json;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteUserDataExportService : IUserDataExportService
{
    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;
    private const string LastExportKey = "habitinator_export_last_v1";
    private const string LastExportTsKey = "habitinator_export_last_v1_ts";

    private readonly IHttpClientFactory _http;
    private readonly ILocalSettingsStore? _localStore;

    public RemoteUserDataExportService(IHttpClientFactory http, ILocalSettingsStore? localStore = null)
    {
        _http = http;
        _localStore = localStore;
    }

    private HttpClient Client => _http.CreateClient("api");

    public async Task<UserDataExportDto> ExportAsync(CancellationToken cancellationToken = default)
    {
        using var res = await Client.GetAsync("api/account/export", cancellationToken);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Sign in first, then try again.");
        }

        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<UserDataExportDto>(Serializer, cancellationToken)
               ?? throw new InvalidOperationException("Empty response from the export API.");
        SaveLastExport(dto);
        return dto;
    }

    public bool TryGetCachedExport(out UserDataExportDto? dto, out DateTimeOffset? exportedAt)
    {
        dto = null;
        exportedAt = null;
        if (_localStore is null)
        {
            return false;
        }

        try
        {
            var raw = _localStore.Read(LastExportKey);
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            dto = JsonSerializer.Deserialize<UserDataExportDto>(raw, Serializer);
            var tsRaw = _localStore.Read(LastExportTsKey);
            if (DateTimeOffset.TryParse(tsRaw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
            {
                exportedAt = ts;
            }
            else
            {
                exportedAt = null;
            }

            return dto is not null;
        }
        catch
        {
            // Corrupt cache reads as a miss. Next export overwrites it.
            return false;
        }
    }

    private void SaveLastExport(UserDataExportDto dto)
    {
        if (_localStore is null)
        {
            return;
        }

        try
        {
            _localStore.Write(LastExportKey, JsonSerializer.Serialize(dto, Serializer));
            _localStore.Write(LastExportTsKey, DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
            // Best effort. Export download still works.
        }
    }
}
