using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;

using MudBlazor;

namespace App.Shared.RCL.Components.Dialogs;

/// <summary>
///     Shared plumbing for the autosave edit dialogs. Every field change is persisted right away.
///     Text fields are debounced. The dialog can be dismissed at any point without losing work.
///     All saves during the session collapse into a single undo entry via <see cref="IUndoService.BeginBatch" />.
/// </summary>
public abstract class AutosaveEditDialogBase<TResult> : ComponentBase, IAsyncDisposable
    where TResult : class
{
    [CascadingParameter] public required IMudDialogInstance MudDialog { get; set; }

    [Inject] protected IUserNotifier Notifier { get; set; } = default!;
    [Inject] protected IUndoService UndoService { get; set; } = default!;
    [Inject] protected IBoardDataService BoardData { get; set; } = default!;

    /// <summary>Label of the single undo entry registered when the dialog session ends.</summary>
    protected abstract string BatchDescription { get; }

    private CancellationTokenSource? _debounceCts;
    private Task _saveChain = Task.CompletedTask;
    private IDisposable? _undoBatch;
    private bool _dirty;
    private bool _sessionEnded;
    private string _saveStatus = string.Empty;

    protected string SaveStatus => _saveStatus;

    protected override void OnInitialized()
    {
        _undoBatch = UndoService.BeginBatch(BatchDescription);
    }

    /// <summary>Queue a debounced save. Used by text fields that fire on every keystroke.</summary>
    protected void ScheduleSave(int delayMs = 500)
    {
        if (_sessionEnded)
        {
            return;
        }

        MarkChanged();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        var previous = _saveChain;
        _saveChain = SaveAfterDelayAsync(previous, delayMs, token);
    }

    /// <summary>Save now with the latest values, serialized behind any save already in flight.</summary>
    protected Task SaveNowAsync()
    {
        if (_sessionEnded)
        {
            return Task.CompletedTask;
        }

        MarkChanged();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
        var previous = _saveChain;
        _saveChain = SaveLatestAsync(previous);
        return _saveChain;
    }

    private void MarkChanged()
    {
        _dirty = true;
        _saveStatus = "saving";
        StateHasChanged();
    }

    private async Task SaveAfterDelayAsync(Task previous, int delayMs, CancellationToken token)
    {
        try
        {
            await previous;
            await Task.Delay(delayMs, token);
            await SaveLatestAsync(Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit. The latest change owns the chain now.
        }
        catch (ObjectDisposedException)
        {
            // Dialog torn down while a debounce timer was pending.
        }
    }

    private async Task SaveLatestAsync(Task previous)
    {
        try
        {
            await previous;
        }
        catch
        {
            // A previous save failed. Still attempt the latest state.
        }

        try
        {
            await SaveValuesAsync(CancellationToken.None);
            _dirty = false;
            _saveStatus = "saved";
        }
        catch (Exception ex)
        {
            _saveStatus = "error";
            await OnSaveErrorAsync(ex);
        }
        finally
        {
            StateHasChanged();
        }
    }

    /// <summary>Persist the dialog's current values.</summary>
    protected abstract Task SaveValuesAsync(CancellationToken cancellationToken = default);

    /// <summary>Build the result payload for a close with the given action.</summary>
    protected abstract TResult BuildResult(EditDialogAction action);

    protected virtual async Task OnSaveErrorAsync(Exception exception)
    {
        await Notifier.NotifyAsync("Could not save changes. Check your connection and try again.", Severity.Warning, CancellationToken.None);
    }

    /// <summary>
    ///     Dismiss the dialog. Changes are already persisted by the autosave pipeline. The final
    ///     debounce burst is flushed during teardown, so closing never blocks on the network.
    /// </summary>
    protected async Task CloseAsync()
    {
        if (_sessionEnded)
        {
            return;
        }

        _sessionEnded = true;
        if (_debounceCts is { } debounceCts)
        {
            await debounceCts.CancelAsync();
            debounceCts.Dispose();
        }

        _debounceCts = null;
        EndSession();
        MudDialog.Close(DialogResult.Ok(BuildResult(EditDialogAction.Close)));
    }

    /// <summary>Close with a non-save action such as archive or delete.</summary>
    protected void CloseWithAction(EditDialogAction action)
    {
        if (_sessionEnded)
        {
            return;
        }

        _sessionEnded = true;
        _debounceCts?.Cancel();
        EndSession();
        MudDialog.Close(DialogResult.Ok(BuildResult(action)));
    }

    /// <summary>
    ///     Runs when the dialog is torn down, for example on escape or backdrop click. Flushes any
    ///     debounced change that did not reach the server yet so nothing is lost.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_debounceCts is { } debounceCts)
        {
            await debounceCts.CancelAsync();
            debounceCts.Dispose();
        }

        _debounceCts = null;

        try
        {
            if (_dirty)
            {
                await SaveValuesAsync(CancellationToken.None);
                _dirty = false;
            }
        }
        catch
        {
            // Dialog is gone. There is nowhere left to surface the error.
        }

        EndSession();
        GC.SuppressFinalize(this);
    }

    private void EndSession()
    {
        _undoBatch?.Dispose();
        _undoBatch = null;
    }
}
