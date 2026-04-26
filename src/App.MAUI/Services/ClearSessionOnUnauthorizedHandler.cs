using System.Net;

namespace App.MAUI.Services;

/// <summary>Clears stored JWT when the API returns 401 so the UI can return to sign-in (expired or revoked token).</summary>
public sealed class ClearSessionOnUnauthorizedHandler : DelegatingHandler
{
    private readonly IApiSession _session;

    public ClearSessionOnUnauthorizedHandler(IApiSession session)
    {
        _session = session;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var res = await base.SendAsync(request, cancellationToken);
        if (res.StatusCode == HttpStatusCode.Unauthorized && _session.IsLoggedIn)
            await _session.ClearSessionAsync(cancellationToken);

        return res;
    }
}
