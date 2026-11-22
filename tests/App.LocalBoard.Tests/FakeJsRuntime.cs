using Microsoft.JSInterop;

namespace App.LocalBoard.Tests;

/// <summary>Accepts the IndexedDB calls the store makes and keeps the last saved payload.</summary>
internal sealed class FakeJsRuntime : IJSRuntime
{
    private string? _savedState;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (identifier)
        {
            case "habBoardStore.save" when args is { Length: > 0 } && args[0] is string raw:
                Interlocked.Exchange(ref _savedState, raw);
                break;
            case "habBoardStore.clear":
                Interlocked.Exchange(ref _savedState, null);
                break;
            default:
                break;
        }

        object? result = identifier == "habBoardStore.load" ? Volatile.Read(ref _savedState) : null;
        return ValueTask.FromResult((TValue)result!);
    }
}
