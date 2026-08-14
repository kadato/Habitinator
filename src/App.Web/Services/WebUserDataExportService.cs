using App.Shared.RCL.Services;
using App.Web.Auth;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebUserDataExportService(
    AuthenticationStateProvider authenticationStateProvider,
    UserDataExportService exportService) : IUserDataExportService
{
    public async Task<UserDataExportDto> ExportAsync(CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = AuthenticatedUserId.TryGet(state.User)
            ?? throw new InvalidOperationException("Sign in required.");
        return await exportService.BuildAsync(userId, cancellationToken);
    }
}
