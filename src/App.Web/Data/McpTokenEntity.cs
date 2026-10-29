namespace App.Web.Data;

/// <summary>Registry row for a personal MCP token. The JWT carries the row id as its jti claim.</summary>
public sealed class McpTokenEntity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Scope { get; set; } = "full";

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }
}
