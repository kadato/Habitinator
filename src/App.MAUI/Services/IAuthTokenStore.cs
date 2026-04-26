namespace App.MAUI.Services;

public interface IAuthTokenStore
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    Task SetAccessTokenAsync(string? token, CancellationToken cancellationToken = default);

    Task<string?> GetEmailAsync(CancellationToken cancellationToken = default);

    Task SetEmailAsync(string? email, CancellationToken cancellationToken = default);
}

public sealed class AuthTokenStore : IAuthTokenStore
{
    private const string TokenKey = "habitinator.jwt";
    private const string EmailKey = "habitinator.email";

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var t = await SecureStorage.GetAsync(TokenKey);
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    public async Task SetAccessTokenAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
            SecureStorage.Remove(TokenKey);
        else
            await SecureStorage.SetAsync(TokenKey, token);
    }

    public async Task<string?> GetEmailAsync(CancellationToken cancellationToken = default)
    {
        return await SecureStorage.GetAsync(EmailKey);
    }

    public async Task SetEmailAsync(string? email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(email))
            SecureStorage.Remove(EmailKey);
        else
            await SecureStorage.SetAsync(EmailKey, email);
    }
}
