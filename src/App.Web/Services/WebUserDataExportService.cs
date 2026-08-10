using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebUserDataExportService(
    AuthenticationStateProvider authenticationStateProvider,
    DemoUserResolver demoUserResolver,
    UserDataExportService exportService) : IUserDataExportService
{
    public async Task<UserDataExportDto> ExportAsync(CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("Sign in required.");
        }

        var userId = await demoUserResolver.ResolveUserIdAsync(user, cancellationToken);
        return await exportService.BuildAsync(userId, cancellationToken);
    }
}
