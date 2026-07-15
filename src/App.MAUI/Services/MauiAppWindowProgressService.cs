using App.Shared.RCL.Services;

namespace App.MAUI.Services;

public class MauiAppWindowProgressService : IAppWindowProgressService
{
    public virtual void SetTitle(string title)
    {
        if (Application.Current != null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var window = Application.Current.Windows.Count > 0 ? Application.Current.Windows[0] : null;
                window?.Title = title;
            });
        }
    }

    public virtual void SetTaskbarTimeBadge(int minutesRemaining)
    {
        // No-op for non-Windows platforms
    }

    public virtual void ClearTaskbarBadge()
    {
        // No-op for non-Windows platforms
    }
}
