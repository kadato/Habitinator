using System.Net;

using App.Shared.RCL.Services;

namespace App.Web.Services;

public sealed class WebAccountActionsService : IAccountActionsService
{
    private readonly HttpClient _http;

    public WebAccountActionsService(HttpClient http)
    {
        _http = http;
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var res = await _http.PostAsJsonAsync("/api/account/change-password",
            new ChangePasswordRequest(currentPassword, newPassword), cancellationToken);
        if (!res.IsSuccessStatusCode)
        {
            var msg = res.StatusCode == HttpStatusCode.BadRequest
                ? "Password change failed. Check your current password."
                : "Password change failed.";
            throw new InvalidOperationException(msg);
        }
    }

    public async Task DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        var res = await _http.PostAsync("/api/account/delete", null, cancellationToken);
        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Account deletion failed.");
        }
    }

    private sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
}
