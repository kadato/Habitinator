using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace App.Shared.RCL.Services;

public static class LocalFirstSaves
{
    /// <summary>Best-effort server save used by the local-first settings services. The local copy is already updated, so failures are logged and swallowed.</summary>
    public static async Task PutBestEffortAsync<T>(
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
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to save {PayloadType} to the server.", typeof(T).Name);
        }
    }
}
