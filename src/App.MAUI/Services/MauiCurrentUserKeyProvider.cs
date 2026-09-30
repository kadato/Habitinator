using App.Shared.RCL.Services.Board.Local;

namespace App.MAUI.Services;

public sealed class MauiCurrentUserKeyProvider(IAuthTokenStore tokens) : ICurrentUserKeyProvider
{
    public async Task<string?> GetUserKeyAsync(CancellationToken cancellationToken = default)
    {
        var email = await tokens.GetEmailAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(email))
        {
            return email.Trim().ToUpperInvariant();
        }

        var jwt = await tokens.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(jwt))
        {
            return null;
        }

        var fromJwt = JwtAccessTokenDisplayClaims.TryGetEmail(jwt);
        return string.IsNullOrWhiteSpace(fromJwt) ? null : fromJwt.Trim().ToUpperInvariant();
    }

    public async Task<bool> HasAuthAsync(CancellationToken cancellationToken = default) =>
        !string.IsNullOrEmpty(await tokens.GetAccessTokenAsync(cancellationToken));
}
