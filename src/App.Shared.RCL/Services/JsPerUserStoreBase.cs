using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

/// <summary>Base for the localStorage-backed stores that key their entries per signed-in account.</summary>
public abstract class JsPerUserStoreBase
{
    private const string JsFile = "_content/App.Shared.RCL/js/boardUiState.js";

    private readonly IJSRuntime _js;
    private readonly IClientSessionProvider _sessionProvider;

    protected JsPerUserStoreBase(IJSRuntime js, IClientSessionProvider sessionProvider)
    {
        _js = js;
        _sessionProvider = sessionProvider;
    }

    protected abstract string BaseKey { get; }

    protected string GetKey()
    {
        var email = _sessionProvider.Email;
        return string.IsNullOrEmpty(email) ? BaseKey : $"{BaseKey}_{email}";
    }

    protected Task EnsureScriptLoadedAsync() =>
        JsInvokeSafe.InvokeVoidAsync(_js, "habitinatorLoadScript", JsFile);
}
