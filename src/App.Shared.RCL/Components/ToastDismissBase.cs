using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace App.Shared.RCL.Components;

/// <summary>Shared tap, swipe, and keyboard dismissal for toast bodies.</summary>
public abstract class ToastDismissBase : ComponentBase
{
    /// <summary>Invoked when the toast body is activated or swiped away.</summary>
    [Parameter] public Func<Task>? OnDismiss { get; set; }

    private double _touchStartX;
    private double _touchStartY;

    protected Task HandleDismiss() => OnDismiss is null ? Task.CompletedTask : OnDismiss();

    protected void HandleTouchStart(TouchEventArgs e)
    {
        if (e.TargetTouches.Length > 0)
        {
            _touchStartX = e.TargetTouches[0].ClientX;
            _touchStartY = e.TargetTouches[0].ClientY;
        }
    }

    protected Task HandleTouchEnd(TouchEventArgs e)
    {
        if (e.ChangedTouches.Length == 0)
        {
            return Task.CompletedTask;
        }

        var dx = e.ChangedTouches[0].ClientX - _touchStartX;
        var dy = e.ChangedTouches[0].ClientY - _touchStartY;
        if (Math.Abs(dx) > 50 && Math.Abs(dx) > Math.Abs(dy))
        {
            return HandleDismiss();
        }

        return Task.CompletedTask;
    }

    protected Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is " " or "Enter")
        {
            return HandleDismiss();
        }

        return Task.CompletedTask;
    }
}
