namespace App.Shared.RCL.Services;

public interface IUndoService
{
    bool CanUndo { get; }
    bool IsUndoing { get; }
    string? LastActionDescription { get; }
    Guid RegisterUndo(string description, Func<Task> undoFunc);

    /// <summary>
    ///     Registers an undo entry with conflict keys describing the state it touches. When an older
    ///     entry is undone out of order, newer entries with overlapping keys are undone first, so the
    ///     target's inverse never restores a value a newer change already replaced.
    /// </summary>
    Guid RegisterUndo(string description, Func<Task> undoFunc, IReadOnlyCollection<string> conflictKeys);

    IDisposable BeginBatch(string description);
    Task UndoAsync();
    Task UndoAsync(Guid actionId);
    event EventHandler? OnStateChanged;
    event EventHandler? OnUndoPerformed;
}
