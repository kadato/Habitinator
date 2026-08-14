using Microsoft.JSInterop;

namespace App.Shared.RCL.Services;

/// <summary>
///     Persists per-column board filters in localStorage so the filters survive navigation
///     and reloads. Keyed per account.
/// </summary>
public sealed class JsBoardColumnStateStore : JsPerUserStoreBase, IBoardColumnStateStore
{
    private readonly IJSRuntime _js;

    public JsBoardColumnStateStore(IJSRuntime js, IClientSessionProvider sessionProvider)
        : base(js, sessionProvider)
    {
        _js = js;
    }

    protected override string BaseKey => "habitinator.columnFilters.v1";

    public Task<BoardColumnFilterState?> GetAsync(CancellationToken cancellationToken = default) =>
        ReadAsync();

    public Task SetAsync(BoardColumnFilterState state, CancellationToken cancellationToken = default) =>
        WriteAsync(state);

    private async Task<BoardColumnFilterState?> ReadAsync()
    {
        // Safe default: no persisted state, fail open. Never throw on JS failure.
        return await JsInvokeSafe.InvokeAsync<BoardColumnFilterState?>(
            _js, "habitinatorGetColumnFilterState", GetKey()).ConfigureAwait(false);
    }

    private async Task WriteAsync(BoardColumnFilterState state)
    {
        await EnsureScriptLoadedAsync();
        await JsInvokeSafe.InvokeVoidAsync(_js, "habitinatorSetColumnFilterState", GetKey(), state);
    }
}
