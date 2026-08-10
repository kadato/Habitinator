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
            return true;
        }
        catch (JSException)
        {
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
            // Safe to ignore; onboarding may show again next visit
        }
    }

    private async Task EnsureScriptLoadedAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("habitinatorLoadScript", JsFile).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // Ignored during navigation
        }
        catch (JSException)
        {
            // Ignored; invoke calls below will fail and be caught too
        }
    }
}
