using System.Text.Json;

using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.Remote;

public sealed class BoardRemoteConflictException : Exception
{
    public string ResponseBody { get; }
    public BoardItem? ServerItem { get; }

    public BoardRemoteConflictException()
        : base("Board API returned 409 Conflict.")
    {
        ResponseBody = string.Empty;
    }

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

    public BoardRemoteConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
        ResponseBody = string.Empty;
    }
}
