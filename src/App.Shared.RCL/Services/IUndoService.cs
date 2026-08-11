namespace App.Shared.RCL.Services;

public interface IUndoService
{
    bool CanUndo { get; }
    bool IsUndoing { get; }
    string? LastActionDescription { get; }
    Guid RegisterUndo(string description, Func<Task> undoFunc);

    /// <summary>
    ///     Registers an undo entry that only touches the given conflict keys. Keys describe what the undo will change.
    ///     When an older action is undone while newer actions are pending, any newer action whose keys
    ///     overlap is undone first (newest first) so state stays consistent. Actions with disjoint keys
    ///     stay pending and their undos only revert their own change.
    /// </summary>
    Guid RegisterUndo(string description, Func<Task> undoFunc, IReadOnlyCollection<string> conflictKeys);

    IDisposable BeginBatch(string description);
    Task UndoAsync();
    Task UndoAsync(Guid actionId);
    event EventHandler? OnStateChanged;
    event EventHandler? OnUndoPerformed;
}
