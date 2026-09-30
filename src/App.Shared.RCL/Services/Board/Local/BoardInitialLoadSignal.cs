namespace App.Shared.RCL.Services.Board.Local;

public sealed class BoardInitialLoadSignal
{
    public bool IsComplete { get; private set; }

    public event EventHandler? Completed;

    public void MarkComplete()
    {
        if (IsComplete)
        {
            return;
        }

        IsComplete = true;
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
