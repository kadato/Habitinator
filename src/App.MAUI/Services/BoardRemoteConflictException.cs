namespace App.MAUI.Services;

/// <summary>HTTP 409 from the board API (version conflict or idempotency fingerprint mismatch).</summary>
public sealed class BoardRemoteConflictException : Exception
{
    public string ResponseBody { get; }

    public BoardRemoteConflictException(string responseBody)
        : base("Board API returned 409 Conflict.")
    {
        ResponseBody = responseBody;
    }
}
