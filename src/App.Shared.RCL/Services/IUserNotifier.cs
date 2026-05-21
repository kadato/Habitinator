using MudBlazor;

namespace App.Shared.RCL.Services;

public interface IUserNotifier
{
    ValueTask NotifyAsync(string message, Severity severity, CancellationToken cancellationToken = default);
}
