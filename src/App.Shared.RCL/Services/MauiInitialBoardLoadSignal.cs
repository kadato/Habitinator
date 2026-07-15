namespace App.Shared.RCL.Services;

/// <summary>Singleton bridge so MAUI background sync can wait for the first board paint (scoped gate is not visible to singleton coordinators).</summary>
public sealed class MauiInitialBoardLoadSignal
{
    public bool IsComplete { get; private set; }

    public event Action? Completed;

    public void MarkComplete()
    {
        if (IsComplete)
        {
            return;
        }

        IsComplete = true;
        Completed?.Invoke();
    }
}
