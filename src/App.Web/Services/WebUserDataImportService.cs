using System.Text.Json;

using App.Shared.RCL.Services;
using App.Web.Auth;

using Microsoft.AspNetCore.Components.Authorization;

namespace App.Web.Services;

public sealed class WebUserDataImportService(
    AuthenticationStateProvider authenticationStateProvider,
    UserDataImportService importService) : IUserDataImportService
{
    public async Task<UserDataImportResult> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var userId = AuthenticatedUserId.TryGet(state.User)
            ?? throw new InvalidOperationException("Sign in required.");
        UserDataExportDto? data;
        try
        {
            data = JsonSerializer.Deserialize<UserDataExportDto>(json, JsonDefaults.Api);
        }
        catch (JsonException)
        {
            throw new UserDataImportValidationException("The file is not valid JSON.");
        }

        if (data is null)
        {
            throw new UserDataImportValidationException("The file is empty or is not a Habitinator export.");
        }

        return await importService.ImportAsync(userId, data, cancellationToken);
    }
}
