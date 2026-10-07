using App.Shared.RCL.Services;

using Microsoft.JSInterop;

namespace App.Web.Client.Services;

internal sealed class WasmLocalSettingsStore : ILocalSettingsStore
{
    private readonly IJSInProcessRuntime? _js;
    private readonly ILogger<WasmLocalSettingsStore> _logger;

    public WasmLocalSettingsStore(IJSRuntime js, ILogger<WasmLocalSettingsStore> logger)
    {
        _js = js as IJSInProcessRuntime;
        _logger = logger;
    }

    public string? Read(string key, string? defaultValue = null)
    {
        if (_js is null)
        {
            return defaultValue;
        }

        try
        {
            var val = _js.Invoke<string?>("localStorage.getItem", key);
            return val ?? defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Local settings read failed for {Key}. The store returns the default.", key);
            return defaultValue;
        }
    }

    public void Write(string key, string value)
    {
        if (_js is null)
        {
            return;
        }

        try
        {
            _js.InvokeVoid("localStorage.setItem", key, value);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Local settings write failed for {Key}. Private browsing quota is a common cause.", key);
        }
    }
}
