namespace App.Shared.RCL.Services;

public class FallbackAppWindowProgressService : IAppWindowProgressService
{
    public virtual void SetTitle(string title)
    {
        // No-op fallback for Web, where <PageTitle> updates the browser tab.
    }

    public virtual void SetTaskbarTimeBadge(int minutesRemaining)
    {
        // No-op fallback
    }

    public virtual void ClearTaskbarBadge()
    {
        // No-op fallback
    }
}
