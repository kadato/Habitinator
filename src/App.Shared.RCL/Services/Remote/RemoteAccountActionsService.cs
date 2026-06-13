using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using App.Shared.RCL.Services;

namespace App.Shared.RCL.Services;

public sealed class RemoteAccountActionsService : IAccountActionsService
{
    private readonly IHttpClientFactory _http;

    public RemoteAccountActionsService(IHttpClientFactory http)
    {
        _http = http;
    }

    private HttpClient Client => _http.CreateClient("api");

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        using var res = await Client.PostAsJsonAsync("api/account/change-password",
            new ChangePasswordRequest(currentPassword, newPassword), cancellationToken);
        res.EnsureSuccessStatusCode();
    }

    public async Task DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        using var res = await Client.PostAsync("api/account/delete", null, cancellationToken);
        res.EnsureSuccessStatusCode();
    }

    private sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
}
