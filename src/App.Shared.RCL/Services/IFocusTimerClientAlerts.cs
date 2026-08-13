namespace App.Shared.RCL.Services;

/// <summary>
///     Plays a short chime and shows a browser Notification when permission allows, for the focus timer.
/// </summary>
public interface IFocusTimerClientAlerts
{
    /// <param name="title">Notification title, e.g. Time's up.</param>
    /// <param name="body">Body text, the summary for the OS notification.</param>
    ValueTask NotifyTimeUpAsync(string title, string body, CancellationToken cancellationToken = default);

    /// <param name="title">Notification title, e.g. Break's over.</param>
    /// <param name="body">Body text, the summary for the OS notification.</param>
    ValueTask NotifyBreakTimeUpAsync(string title, string body, CancellationToken cancellationToken = default);
}
