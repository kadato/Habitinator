using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebUserDataExportService(
    AuthenticationStateProvider authenticationStateProvider,
    CurrentUserAccessor currentUserAccessor,
    UserDataExportService exportService) : IUserDataExportService
{
    public async Task<UserDataExportDto> ExportAsync(CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = await currentUserAccessor.ResolveAsync(state.User, cancellationToken);
        return await exportService.BuildAsync(userId, cancellationToken);
    }
}
