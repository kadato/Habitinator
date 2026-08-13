namespace App.Shared.RCL.Services;

/// <summary>Signals when the home board has finished its first data load, used to defer non-critical startup work.</summary>
public interface IInitialBoardLoadGate
{
    bool IsComplete { get; }

    void MarkComplete();

    event EventHandler? Completed;
}

public sealed class InitialBoardLoadGate : IInitialBoardLoadGate
{
    private readonly MauiInitialBoardLoadSignal? _mauiSignal;
    private bool _isComplete;

    public InitialBoardLoadGate()
    {
    }

    public InitialBoardLoadGate(MauiInitialBoardLoadSignal mauiSignal)
    {
        _mauiSignal = mauiSignal;
    }

    public bool IsComplete => _isComplete;

    public event EventHandler? Completed;

    public void MarkComplete()
    {
        if (_isComplete)
        {
            return;
        }

        _isComplete = true;
        _mauiSignal?.MarkComplete();
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
