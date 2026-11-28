using System.Text.Json;

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
        if (res.IsSuccessStatusCode)
        {
            return;
        }

        var detail = "Password change failed. Check your current password.";
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            if (root.TryGetProperty("detail", out var element)
                && element.ValueKind == JsonValueKind.String
                && element.GetString() is { Length: > 0 } serverDetail)
            {
                detail = serverDetail;
            }
            else if (root.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Object
                && errors.TryGetProperty("NewPassword", out var newPasswordErrors)
                && newPasswordErrors.ValueKind == JsonValueKind.Array
                && newPasswordErrors.GetArrayLength() > 0)
            {
                // Model validation runs before the endpoint, so a short new password
                // never reaches Identity. Give the user a plain sentence instead of
                // the framework's field-name message.
                detail = "New password must be at least 8 characters.";
            }
        }
        catch
        {
            // Keep the default message when the body is missing or malformed.
        }

        throw new InvalidOperationException(detail);
    }

    public async Task DeleteAccountAsync(string password, CancellationToken cancellationToken = default)
    {
        using var res = await Client.PostAsJsonAsync(
            "api/account/delete",
            new DeleteAccountRequest(password),
            JsonDefaults.Api,
            cancellationToken);
        if (res.IsSuccessStatusCode)
        {
            return;
        }

        var detail = "Account deletion failed. Check your current password.";
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            if (root.TryGetProperty("detail", out var element)
                && element.ValueKind == JsonValueKind.String
                && element.GetString() is { Length: > 0 } serverDetail)
            {
                detail = serverDetail;
            }
            else if (root.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Object
                && errors.TryGetProperty("Password", out var passwordErrors)
                && passwordErrors.ValueKind == JsonValueKind.Array
                && passwordErrors.GetArrayLength() > 0)
            {
                detail = "Enter your current password.";
            }
        }
        catch
        {
            // Keep the default message when the body is missing or malformed.
        }

        throw new InvalidOperationException(detail);
    }

    public async Task<CreateMcpTokenResponse> CreateMcpTokenAsync(CreateMcpTokenRequest? request = null, CancellationToken cancellationToken = default)
    {
        using var res = await Client.PostAsJsonAsync("api/account/mcp-token",
            request ?? new CreateMcpTokenRequest(), JsonDefaults.Api, cancellationToken);
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadFromJsonAsync<CreateMcpTokenResponse>(JsonDefaults.Api, cancellationToken);
        return data ?? throw new InvalidOperationException("Failed to generate MCP token.");
    }

    public async Task<IReadOnlyList<McpTokenInfo>> ListMcpTokensAsync(CancellationToken cancellationToken = default)
    {
        using var res = await Client.GetAsync("api/account/mcp-tokens", cancellationToken);
        res.EnsureSuccessStatusCode();
        var data = await res.Content.ReadFromJsonAsync<List<McpTokenInfo>>(JsonDefaults.Api, cancellationToken);
        return data ?? [];
    }

    public async Task RevokeMcpTokenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var res = await Client.DeleteAsync($"api/account/mcp-tokens/{id:D}", cancellationToken);
        res.EnsureSuccessStatusCode();
    }

    public async Task RevokeAllMcpTokensAsync(CancellationToken cancellationToken = default)
    {
        using var res = await Client.PostAsync("api/account/mcp-tokens/revoke-all", null, cancellationToken);
        res.EnsureSuccessStatusCode();
    }
}
