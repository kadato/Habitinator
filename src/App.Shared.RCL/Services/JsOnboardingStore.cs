using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

/// <summary>Remembers whether the first-run onboarding dialog was shown for the signed-in account.</summary>
public sealed class JsOnboardingStore : JsPerUserStoreBase, IOnboardingStore
{
    private readonly IJSRuntime _js;

    public JsOnboardingStore(IJSRuntime js, IClientSessionProvider sessionProvider)
        : base(js, sessionProvider)
    {
        _js = js;
    }

    protected override string BaseKey => "habitinator.onboarding.v1";

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

    public Task MarkCompletedAsync(CancellationToken cancellationToken = default)
    {
        return JsInvokeSafe.InvokeVoidAsync(_js, "habitinatorSetOnboardingDone", GetKey(), true);
    }
}
