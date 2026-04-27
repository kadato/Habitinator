namespace App.MAUI.Services.LocalBoard;

/// <summary>Clears SQLite mirror + outbox on sign-out.</summary>
public interface IMauiBoardLocalStoreLifecycle
{
    Task ClearAllLocalStateAsync(CancellationToken cancellationToken = default);
}
