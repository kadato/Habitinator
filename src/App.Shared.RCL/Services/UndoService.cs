using App.Shared.RCL.Components;

using Microsoft.Extensions.Logging;

using MudBlazor;

namespace App.Shared.RCL.Services;

public sealed class UndoService : IUndoService, IDisposable
{
    private const string UndoSnackbarKeyPrefix = "habitinator-undo";

    private readonly List<UndoAction> _undoStack = [];
    private readonly ISnackbar _snackbar;
    private readonly INotificationSettingsService _settingsService;
    private readonly INotificationSettingsRules _notificationRules;
    private readonly ILogger<UndoService> _logger;

    private List<Func<Task>>? _currentBatch;
    private List<string>? _currentBatchKeys;
    private string? _currentBatchDescription;
    private int _undoingCount;
    private readonly SemaphoreSlim _undoLock = new(1, 1);

    public bool IsUndoing => _undoingCount > 0;
    public bool CanUndo => _undoStack.Count > 0;
    public string? LastActionDescription => _undoStack.Count > 0 ? _undoStack[^1].Description : null;

    public event EventHandler? OnStateChanged;
    public event EventHandler? OnUndoPerformed;

    public UndoService(
        ISnackbar snackbar,
        INotificationSettingsService settingsService,
        INotificationSettingsRules notificationRules,
        ILogger<UndoService> logger)
    {
        _snackbar = snackbar;
        _settingsService = settingsService;
        _notificationRules = notificationRules;
        _logger = logger;
    }

    public Guid RegisterUndo(string description, Func<Task> undoFunc)
    {
        return RegisterUndo(description, undoFunc, []);
    }

    public Guid RegisterUndo(string description, Func<Task> undoFunc, IReadOnlyCollection<string> conflictKeys)
    {
        if (IsUndoing)
        {
            return Guid.Empty;
        }

        if (_currentBatch is { } batch && _currentBatchKeys is { } keys)
        {
            batch.Add(undoFunc);
            foreach (var key in conflictKeys)
            {
                keys.Add(key);
            }
            return Guid.Empty;
        }

        var action = new UndoAction(description, undoFunc, conflictKeys);
        action.SnackbarKey = $"{UndoSnackbarKeyPrefix}-{action.Id:N}";

        _undoStack.Add(action);
        OnStateChanged?.Invoke(this, EventArgs.Empty);
        _ = ShowUndoSnackbarAsync(action);
        return action.Id;
    }

    public IDisposable BeginBatch(string description)
    {
        return new UndoBatch(this, description);
    }

    private void StartBatch(string description)
    {
        _currentBatch = [];
        _currentBatchKeys = [];
        _currentBatchDescription = description;
    }

    private void EndBatch()
    {
        if (_currentBatch is null)
        {
            return;
        }

        var batch = _currentBatch;
        var keys = _currentBatchKeys ?? [];
        var desc = _currentBatchDescription ?? "Multiple actions";
        _currentBatch = null;
        _currentBatchKeys = null;
        _currentBatchDescription = null;

        if (batch.Count > 0)
        {
            batch.Reverse();
            RegisterUndo(desc, async () =>
            {
                foreach (var batchAction in batch)
                {
                    await batchAction().ConfigureAwait(false);
                }
            }, keys);
        }
    }

    public Task UndoAsync() => UndoAsync(null);

    public Task UndoAsync(Guid actionId) => UndoAsync((Guid?)actionId);

    private async Task UndoAsync(Guid? actionId)
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        await _undoLock.WaitAsync().ConfigureAwait(false);
        List<UndoAction>? undone = null;
        try
        {
            var index = actionId is null
                ? _undoStack.Count - 1
                : _undoStack.FindIndex(a => a.Id == actionId);

            if (index < 0)
            {
                return;
            }

            var target = _undoStack[index];

            // Undoing an older action out of order: any newer action that may touch the same state must
            // be undone first, newest first, so the target's inverse applies to a consistent snapshot.
            // Newer actions with disjoint keys are left pending. Their undos only revert their own change.
            var toUndo = new List<UndoAction>();
            if (actionId is not null)
            {
                for (var i = _undoStack.Count - 1; i > index; i--)
                {
                    var newer = _undoStack[i];
                    if (MayConflict(target, newer))
                    {
                        toUndo.Add(newer);
                    }
                }
            }

            toUndo.Add(target);

            Interlocked.Increment(ref _undoingCount);
            try
            {
                foreach (var action in toUndo)
                {
                    await action.UndoFunc().ConfigureAwait(false);
                }

                // Only drop actions from the stack once their undo succeeded, so a failed
                // undo can be retried instead of being lost.
                foreach (var action in toUndo)
                {
                    _undoStack.Remove(action);
                }

                undone = toUndo;
                OnUndoPerformed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                Interlocked.Decrement(ref _undoingCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Undo failed; the action remains on the undo stack.");
        }
        finally
        {
            _undoLock.Release();
            if (undone is not null)
            {
                foreach (var action in undone)
                {
                    DismissSnackbar(action);
                }
            }

            OnStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool MayConflict(UndoAction a, UndoAction b)
    {
        if (a.ConflictKeys.Count == 0 || b.ConflictKeys.Count == 0)
        {
            // Unknown scope: assume it may touch anything so we never undo out of order against it.
            return true;
        }

        return a.ConflictKeys.Any(ka => b.ConflictKeys.Any(kb =>
            ka == kb
            || ka.StartsWith(kb + ":", StringComparison.Ordinal)
            || kb.StartsWith(ka + ":", StringComparison.Ordinal)));
    }

    private async Task ShowUndoSnackbarAsync(UndoAction action)
    {
        try
        {
            var settings = await _settingsService.GetAsync(CancellationToken.None).ConfigureAwait(false);
            var ms = _notificationRules.UndoVisibleStateDurationMs(settings.ToastDuration);

            Snackbar? toast = null;
            toast = _snackbar.Add<UndoToastContent>(
                new Dictionary<string, object>
                {
                    [nameof(UndoToastContent.Description)] = action.Description,
                    [nameof(UndoToastContent.OnUndo)] = new Func<Task>(async () =>
                    {
                        await UndoAsync(action.Id).ConfigureAwait(false);
                        toast?.ForceClose();
                    }),
                    [nameof(UndoToastContent.OnDismiss)] = new Func<Task>(() =>
                    {
                        toast?.ForceClose();
                        return Task.CompletedTask;
                    }),
                },
                Severity.Normal,
                config =>
                {
                    AppSnackbar.Configure(config, ms);
                    config.SnackbarTypeClass = $"{AppSnackbar.ToastTypeClass} undo-toast";
                },
                action.SnackbarKey);
        }
        catch (Exception ex)
        {
            // Best-effort snackbar. The action is still on the undo stack
            _logger.LogDebug(ex, "Failed to show the undo snackbar.");
        }
    }

    private void DismissSnackbar(UndoAction action)
    {
        if (action.SnackbarKey is null)
        {
            return;
        }

        try
        {
            _snackbar.RemoveByKey(action.SnackbarKey);
        }
        catch (Exception ex)
        {
            // Best-effort dismissal
            _logger.LogDebug(ex, "Failed to dismiss the undo snackbar.");
        }
    }

    private sealed class UndoAction
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Description { get; }
        public Func<Task> UndoFunc { get; }
        public IReadOnlyCollection<string> ConflictKeys { get; }
        public string? SnackbarKey { get; set; }

        public UndoAction(string description, Func<Task> undoFunc, IReadOnlyCollection<string> conflictKeys)
        {
            Description = description;
            UndoFunc = undoFunc;
            ConflictKeys = conflictKeys;
        }
    }

    public void Dispose()
    {
        _undoLock.Dispose();
    }

    private sealed class UndoBatch : IDisposable
    {
        private readonly UndoService _service;

        public UndoBatch(UndoService service, string description)
        {
            _service = service;
            _service.StartBatch(description);
        }

        public void Dispose()
        {
            _service.EndBatch();
        }
    }
}
