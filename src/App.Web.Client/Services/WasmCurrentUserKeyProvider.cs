using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;

namespace App.Web.Client.Services;

public sealed class WasmCurrentUserKeyProvider(IClientSessionProvider session) : ICurrentUserKeyProvider
{
    public Task<string?> GetUserKeyAsync(CancellationToken cancellationToken = default)
    {
        var email = session.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(email.Trim().ToUpperInvariant());
    }

    public Task<bool> HasAuthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(session.IsLoggedIn);
    }
}
