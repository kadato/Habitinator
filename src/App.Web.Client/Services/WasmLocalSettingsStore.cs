using App.Shared.RCL.Services;

using Microsoft.JSInterop;

namespace App.Web.Client.Services;

internal sealed class WasmLocalSettingsStore : ILocalSettingsStore
{
    private readonly IJSInProcessRuntime? _js;

    public WasmLocalSettingsStore(IJSRuntime js)
    {
        _js = js as IJSInProcessRuntime;
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
        catch
        {
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
        catch
        {
            // Ignore storage errors in browser (e.g. private browsing storage limits)
        }
    }
}
