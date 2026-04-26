using MudBlazor;

namespace App.Shared.RCL.Services;

public interface IUserNotifier
{
    ValueTask NotifyAsync(string message, Severity severity, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Shown when the session stopwatch hits an optional "time's up" duration; gated by
    ///     <see cref="NotificationSettings.FocusTimerAlertsEnabled" /> and in-app messages (not the general success-toast
    ///     toggle).
    /// </summary>
    ValueTask NotifyFocusTimerEndAsync(string message, CancellationToken cancellationToken = default);
}
