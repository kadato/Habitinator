using ModelContextProtocol;

namespace App.Web.Mcp;

/// <summary>
/// Validation failure for one named tool field. The message always names the field,
/// states the expected format, and includes the received value (truncated), so
/// headless agents can fix the input without another round trip.
/// Never put tokens or secrets into these messages.
/// </summary>
public class McpFieldException(string field, string details)
    : McpProtocolException($"{field}: {details}", McpErrorCode.InvalidParams)
{
    public string Field { get; } = field;
}

/// <summary>
/// Warning that a similarly titled active item already exists.
/// Thrown instead of creating when allowDuplicate is false; the message names
/// the existing title and id and tells the caller how to create anyway.
/// </summary>
public sealed class DuplicateTitleException(Guid existingId, string existingTitle)
    : McpFieldException("title",
        $"Similar item exists: '{existingTitle}' (id: {existingId:D}). Retry with allowDuplicate=true to create anyway.")
{
    public Guid ExistingId { get; } = existingId;

    public string ExistingTitle { get; } = existingTitle;
}
