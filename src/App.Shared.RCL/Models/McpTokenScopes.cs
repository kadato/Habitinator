namespace App.Shared.RCL.Models;

/// <summary>Access scopes for MCP personal tokens.</summary>
public static class McpTokenScopes
{
    public const string Full = "full";
    public const string ReadOnly = "read_only";
    public const string TrackingOnly = "tracking_only";

    public static readonly string[] All = [Full, TrackingOnly, ReadOnly];

    public static string Normalize(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return Full;
        }

        var clean = scope.Trim().ToLowerInvariant();
        return clean switch
        {
            "readonly" or "read_only" or "read" => ReadOnly,
            "trackingonly" or "tracking_only" or "tracking" => TrackingOnly,
            _ => Full,
        };
    }

    public static string DisplayName(string? scope) => Normalize(scope) switch
    {
        ReadOnly => "Read only",
        TrackingOnly => "Tracking only",
        _ => "Full access",
    };
}
