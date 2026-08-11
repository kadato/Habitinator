using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

/// <summary>
///     Persists per-column board filters in localStorage so the filters survive navigation
///     and reloads. Keyed per account.
/// </summary>
public sealed class JsBoardColumnStateStore : IBoardColumnStateStore
{
    private const string JsFile = "_content/App.Shared.RCL/js/boardUiState.js";
    private const string BaseKey = "habitinator.columnFilters.v1";

    private readonly IJSRuntime _js;
    private readonly IClientSessionProvider _sessionProvider;

    public JsBoardColumnStateStore(IJSRuntime js, IClientSessionProvider sessionProvider)
    {
        _js = js;
        _sessionProvider = sessionProvider;
    }

    private string GetKey()
    {
        var email = _sessionProvider.Email;
        return string.IsNullOrEmpty(email) ? BaseKey : $"{BaseKey}_{email}";
    }

    public async Task<BoardColumnFilterState?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureScriptLoadedAsync();
            return await _js.InvokeAsync<BoardColumnFilterState?>("habitinatorGetColumnFilterState", GetKey())
                .ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // Safe default: no persisted state, fail open. Never throw on JS failure.
            return null;
        }
        catch (JSException)
        {
            // Safe default: no persisted state, fail open. Never throw on JS failure.
            return null;
        }
    }

    public async Task SetAsync(BoardColumnFilterState state, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureScriptLoadedAsync();
            await _js.InvokeVoidAsync("habitinatorSetColumnFilterState", GetKey(), state).ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // Safe to ignore during navigation/disposal
        }
        catch (JSException)
        {
            // Safe to ignore; filters simply won't persist
        }
    }

    private Task EnsureScriptLoadedAsync()
    {
        return JsInvokeSafe.InvokeVoidAsync(_js, "habitinatorLoadScript", JsFile);
    }
}
