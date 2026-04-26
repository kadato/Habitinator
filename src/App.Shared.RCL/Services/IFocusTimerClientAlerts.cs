namespace App.Shared.RCL.Services;

/// <summary>
///     Plays a short chime and shows a browser Notification (when permission allows) for the focus timer.
/// </summary>
public interface IFocusTimerClientAlerts
{
    /// <param name="title">Notification title (e.g. Time's up).</param>
    /// <param name="body">Body text (summary for OS notification).</param>
    ValueTask NotifyTimeUpAsync(string title, string body, CancellationToken cancellationToken = default);
}
