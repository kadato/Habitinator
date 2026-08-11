using System.Text.Json;
using System.Text.Json.Serialization;

namespace App.Shared.RCL.Services;

/// <summary>Shared JSON serialization options used across the app.</summary>
public static class JsonDefaults
{
    /// <summary>Web/API conventions: camelCase, case-insensitive reads, nulls omitted on write.</summary>
    public static readonly JsonSerializerOptions Api = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>CamelCase for local persistence.</summary>
    public static readonly JsonSerializerOptions Storage = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Human-readable export files. Default naming, indented output.</summary>
    public static readonly JsonSerializerOptions Export = new()
    {
        WriteIndented = true
    };
}
