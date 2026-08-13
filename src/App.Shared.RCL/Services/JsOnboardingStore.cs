using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

/// <summary>Remembers whether the first-run onboarding dialog was shown for the signed-in account.</summary>
public sealed class JsOnboardingStore : IOnboardingStore
{
    private const string JsFile = "_content/App.Shared.RCL/js/boardUiState.js";
    private const string BaseKey = "habitinator.onboarding.v1";

    private readonly IJSRuntime _js;
    private readonly IClientSessionProvider _sessionProvider;

    public JsOnboardingStore(IJSRuntime js, IClientSessionProvider sessionProvider)
    {
        _js = js;
        _sessionProvider = sessionProvider;
    }

    private string GetKey()
    {
        var email = _sessionProvider.Email;
        return string.IsNullOrEmpty(email) ? BaseKey : $"{BaseKey}_{email}";
    }

    public async Task<bool> IsCompletedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureScriptLoadedAsync();
            return await _js.InvokeAsync<bool>("habitinatorGetOnboardingDone", GetKey()).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // Safe default: treat onboarding as completed, fail closed. Never throw on JS failure.
            return true;
        }
        catch (JSException)
        {
            // Safe default: treat onboarding as completed, fail closed. Never throw on JS failure.
            return true;
        }
    }

    public async Task MarkCompletedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureScriptLoadedAsync();
            await _js.InvokeVoidAsync("habitinatorSetOnboardingDone", GetKey(), true).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // Safe to ignore during navigation/disposal
        }
        catch (JSException)
        {
            // Safe to ignore. Onboarding may show again next visit
        }
    }

    private Task EnsureScriptLoadedAsync()
    {
        return JsInvokeSafe.InvokeVoidAsync(_js, "habitinatorLoadScript", JsFile);
    }
}
