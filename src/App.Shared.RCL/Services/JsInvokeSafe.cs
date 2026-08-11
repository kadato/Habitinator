using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

/// <summary>Invokes JS interop and swallows the exceptions that occur during navigation, disposal, or JS failure.</summary>
public static class JsInvokeSafe
{
    public static async Task InvokeVoidAsync(IJSRuntime js, string identifier, params object?[]? args)
    {
        try
        {
            await js.InvokeVoidAsync(identifier, args).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // Ignored during page navigation or component disposal
        }
        catch (JSException)
        {
            // Ignored when the browser-side script is unavailable
        }
        catch (TaskCanceledException)
        {
            // Ignored when the circuit is torn down mid-invoke
        }
        catch (InvalidOperationException)
        {
            // Ignored when the JS runtime is not ready
        }
    }

    public static async Task<T?> InvokeAsync<T>(IJSRuntime js, string identifier, params object?[]? args)
    {
        try
        {
            return await js.InvokeAsync<T>(identifier, args).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            return default;
        }
        catch (JSException)
        {
            return default;
        }
        catch (TaskCanceledException)
        {
            return default;
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }
}
