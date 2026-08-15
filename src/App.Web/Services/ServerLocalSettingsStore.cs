using App.Shared.RCL.Services;

namespace App.Web.Services;

/// <summary>
///     In-memory settings store for server-side prerendering. The interactive
///     session runs in the browser and persists through the WASM store, so
///     nothing written here needs to outlive the request.
/// </summary>
public sealed class ServerLocalSettingsStore : ILocalSettingsStore
{
    private readonly Dictionary<string, string> _values = [];

    public string? Read(string key, string? defaultValue = null)
    {
        return _values.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public void Write(string key, string value)
    {
        _values[key] = value;
    }
}
