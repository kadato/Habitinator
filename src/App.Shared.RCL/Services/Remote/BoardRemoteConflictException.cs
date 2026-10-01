using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.Remote;

public sealed class BoardRemoteConflictException : Exception
{
    public string ResponseBody { get; }
    public BoardItem? ServerItem { get; }

    /// <summary>Server clock from the 409 response's Date header, used to correct device clock skew.</summary>
    public DateTimeOffset? ServerTimeUtc { get; }

    public BoardRemoteConflictException(string responseBody, DateTimeOffset? serverTimeUtc = null)
        : base("Board API returned 409 Conflict.")
    {
        ResponseBody = responseBody;
        ServerTimeUtc = serverTimeUtc;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("item", out var itemEl))
            {
                ServerItem = JsonSerializer.Deserialize<BoardItem>(itemEl.GetRawText(), BoardOutboxJson.Options);
            }
        }
        catch
        {
            // Ignore parse errors if payload isn't structured as expected
        }
    }
}
