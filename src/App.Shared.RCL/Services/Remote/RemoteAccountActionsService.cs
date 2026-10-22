using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.Remote;

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

    public async Task<CreateMcpTokenResponse> CreateMcpTokenAsync(CreateMcpTokenRequest? request = null, CancellationToken cancellationToken = default)
    {
        using var res = await Client.PostAsJsonAsync("api/account/mcp-token",
            request ?? new CreateMcpTokenRequest(), JsonDefaults.Api, cancellationToken);
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadFromJsonAsync<CreateMcpTokenResponse>(JsonDefaults.Api, cancellationToken);
        return data ?? throw new InvalidOperationException("Failed to generate MCP token.");
    }
}
