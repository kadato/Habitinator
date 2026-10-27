using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

public static class LocalFirstSaves
{
    /// <summary>Best-effort server save used by the local-first settings services. The local copy already has the update. The logger logs failures.</summary>
    public static async Task PutBestEffortAsync<T>(
        HttpClient client,
        string relativeUrl,
        T payload,
        JsonSerializerOptions serializer,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        _ = await TryPutAsync(client, relativeUrl, payload, serializer, logger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>This method sends one PUT and returns true on success. When this method returns false, queue the payload.</summary>
    public static async Task<bool> TryPutAsync<T>(
        HttpClient client,
        string relativeUrl,
        T payload,
        JsonSerializerOptions serializer,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            using var res = await client
                .PutAsJsonAsync(relativeUrl, payload, serializer, cancellationToken)
                .ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to save {PayloadType} to the server.", typeof(T).Name);
            return false;
        }
    }
}
