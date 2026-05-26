namespace App.MAUI.Services.LocalBoard;

/// <summary>Clears SQLite mirror + outbox on sign-out.</summary>
public interface IMauiBoardLocalStoreLifecycle
{
    Task EnsureStoreReadyAsync(CancellationToken cancellationToken = default);

    Task ClearAllLocalStateAsync(CancellationToken cancellationToken = default);
}
