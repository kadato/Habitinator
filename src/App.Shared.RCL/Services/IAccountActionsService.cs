namespace App.Shared.RCL.Services;

public interface IAccountActionsService
{
    Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    Task DeleteAccountAsync(CancellationToken cancellationToken = default);
}
