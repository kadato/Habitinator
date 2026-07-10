namespace App.Shared.RCL.Services;

public interface IAppWindowProgressService
{
    void SetTitle(string title);
    void SetTaskbarTimeBadge(int minutesRemaining);
    void ClearTaskbarBadge();
}
