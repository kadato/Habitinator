using System.Net;
using System.Text;
using System.Text.Json;

using App.Shared.RCL.Services.Board.Local;

namespace App.Shared.RCL.Services.Remote;

public sealed class RemoteUserDataImportService(
    IHttpClientFactory http,
    IBoardLocalStoreLifecycle localLifecycle,
    IBoardSyncRequestor syncRequestor,
    ILocalSettingsStore? localStore = null) : IUserDataImportService
{
    private static readonly JsonSerializerOptions Serializer = JsonDefaults.Api;
    private const string PendingImportKey = "habitinator_import_pending_v1";

    private HttpClient Client => http.CreateClient("api");

    public async Task<UserDataImportResult> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        try
        {
            using var res = await Client.PostAsync(
                "api/account/import",
                new StringContent(json, Encoding.UTF8, "application/json"),
                cancellationToken);
            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException("Sign in first, then try again.");
            }

            if (res.StatusCode == HttpStatusCode.BadRequest)
            {
                throw new InvalidOperationException(await ReadDetailAsync(res, cancellationToken));
            }

            res.EnsureSuccessStatusCode();
            var result = await res.Content.ReadFromJsonAsync<UserDataImportResult>(Serializer, cancellationToken)
                ?? throw new InvalidOperationException("Empty response from the import API.");
            ClearPendingImport();
            try
            {
                await localLifecycle.ClearAllLocalStateAsync(cancellationToken);
            }
            catch
            {
                // The mirror rebuilds from the server on the next load either way.
            }

            syncRequestor.RequestSync();
            return result;
        }
        catch (HttpRequestException ex)
        {
            SavePendingImport(json);
            throw new InvalidOperationException("You are offline. The client saved the backup here. The backup restores on reconnect.", ex);
        }
    }

    public bool HasPendingImport()
    {
        if (localStore is null)
        {
            return false;
        }

        try
        {
            return !string.IsNullOrEmpty(localStore.Read(PendingImportKey));
        }
        catch
        {
            return false;
        }
    }

    public async Task<UserDataImportResult> FlushPendingImportAsync(CancellationToken cancellationToken = default)
    {
        string? raw;
        try
        {
            raw = localStore?.Read(PendingImportKey);
        }
        catch
        {
            raw = null;
        }

        if (string.IsNullOrEmpty(raw))
        {
            throw new InvalidOperationException("No pending import.");
        }

        var result = await ImportAsync(raw, cancellationToken);
        return result;
    }

    private void SavePendingImport(string json)
    {
        if (localStore is null)
        {
            return;
        }

        try
        {
            localStore.Write(PendingImportKey, json);
        }
        catch
        {
            // Best effort. Caller still surfaces the offline message.
        }
    }

    private void ClearPendingImport()
    {
        if (localStore is null)
        {
            return;
        }

        try
        {
            localStore.Write(PendingImportKey, string.Empty);
        }
        catch
        {
            // Best effort.
        }
    }

    private static async Task<string> ReadDetailAsync(HttpResponseMessage res, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
            if (doc.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.GetString() ?? "The export file failed validation.";
            }
        }
        catch
        {
            // Fall through to the default message.
        }

        return "The export file failed validation.";
    }
}
