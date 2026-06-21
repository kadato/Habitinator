namespace App.MAUI.Services;

public interface IAuthTokenStore
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    Task SetAccessTokenAsync(string? token, CancellationToken cancellationToken = default);

    Task<string?> GetEmailAsync(CancellationToken cancellationToken = default);

    Task SetEmailAsync(string? email, CancellationToken cancellationToken = default);
}

/// <summary>
///     Unpackaged Windows <see cref="SecureStorage" /> uses a single <c>securestorage.dat</c> with exclusive create;
///     parallel HTTP 401s (see <see cref="ClearSessionOnUnauthorizedHandler" />) must not run
///     <c>Remove</c>/<c>Set</c> concurrently.
/// </summary>
public sealed class AuthTokenStore : IAuthTokenStore
{
    private const int IoRetries = 3;
    private const string TokenKey = "habitinator.jwt";
    private const string EmailKey = "habitinator.email";
    private static readonly SemaphoreSlim StorageLock = new(1, 1);

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return GetAsyncInternal(TokenKey, cancellationToken);
    }

    public Task SetAccessTokenAsync(string? token, CancellationToken cancellationToken = default)
    {
        return SetOrRemoveAsync(TokenKey, token, cancellationToken);
    }

    public async Task<string?> GetEmailAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsyncInternal(EmailKey, cancellationToken);
    }

    public Task SetEmailAsync(string? email, CancellationToken cancellationToken = default)
    {
        return SetOrRemoveAsync(EmailKey, email, cancellationToken);
    }

    private static Task<string?> GetAsyncInternal(string key, CancellationToken cancellationToken)
    {
        return RunSerializedAsync(
            async () =>
            {
                string? t = await SecureStorage.GetAsync(key);
                return string.IsNullOrWhiteSpace(t) ? null : t;
            },
            cancellationToken);
    }

    private static async Task SetOrRemoveAsync(string key, string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(value))
        {
            await RunSerializedAsync(
                () =>
                {
                    SecureStorage.Remove(key);
                    return Task.CompletedTask;
                },
                cancellationToken);
        }
        else
        {
            await RunSerializedAsync(() => SecureStorage.SetAsync(key, value), cancellationToken);
        }
    }

    private static async Task<T> RunSerializedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await StorageLock.WaitAsync(cancellationToken);
        try
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    return await action();
                }
                catch (IOException) when (attempt < IoRetries)
                {
                    attempt++;
                    await Task.Delay(20 * attempt, cancellationToken);
                }
            }
        }
        finally
        {
            StorageLock.Release();
        }
    }

    private static async Task RunSerializedAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await StorageLock.WaitAsync(cancellationToken);
        try
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    await action();
                    return;
                }
                catch (IOException) when (attempt < IoRetries)
                {
                    attempt++;
                    await Task.Delay(20 * attempt, cancellationToken);
                }
            }
        }
        finally
        {
            StorageLock.Release();
        }
    }
}
