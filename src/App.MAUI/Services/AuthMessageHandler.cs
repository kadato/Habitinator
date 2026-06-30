using System.Net.Http.Headers;

namespace App.MAUI.Services;

public sealed partial class AuthMessageHandler(IAuthTokenStore tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var t = await tokens.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(t))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
