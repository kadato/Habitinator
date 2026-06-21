using System.Net.Http.Headers;

namespace App.MAUI.Services;

public sealed class AuthMessageHandler : DelegatingHandler
{
    private readonly IAuthTokenStore _tokens;

    public AuthMessageHandler(IAuthTokenStore tokens)
    {
        _tokens = tokens;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var t = await _tokens.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(t))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
