using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>HTTP 409 from the board API (version conflict or idempotency fingerprint mismatch).</summary>
public sealed class BoardRemoteConflictException : Exception
{
    public string ResponseBody { get; }
    public BoardItem? ServerItem { get; }

    public BoardRemoteConflictException(string responseBody)
        : base("Board API returned 409 Conflict.")
    {
        ResponseBody = responseBody;
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
