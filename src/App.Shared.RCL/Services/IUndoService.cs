using System;
using System.Threading.Tasks;

namespace App.Shared.RCL.Services;

public interface IUndoService
{
    bool CanUndo { get; }
    bool IsUndoing { get; }
    string? LastActionDescription { get; }
    void RegisterUndo(string description, Func<Task> undoFunc);
    IDisposable BeginBatch(string description);
    Task UndoAsync();
    void Clear();
    event Action? OnStateChanged;
    event Action? OnUndoPerformed;
}
