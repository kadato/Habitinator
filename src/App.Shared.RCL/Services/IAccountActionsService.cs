using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public interface IAccountActionsService
{
    Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    Task DeleteAccountAsync(CancellationToken cancellationToken = default);

    Task<CreateMcpTokenResponse> CreateMcpTokenAsync(CreateMcpTokenRequest? request = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpTokenInfo>> ListMcpTokensAsync(CancellationToken cancellationToken = default);

    Task RevokeMcpTokenAsync(Guid id, CancellationToken cancellationToken = default);

    Task RevokeAllMcpTokensAsync(CancellationToken cancellationToken = default);
}
