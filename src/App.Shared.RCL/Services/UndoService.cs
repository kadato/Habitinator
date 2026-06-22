using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using App.Shared.RCL.Components;
using App.Shared.RCL.Models;

using MudBlazor;

namespace App.Shared.RCL.Services;

public sealed class UndoService : IUndoService, IDisposable
{
    private const string UndoSnackbarKeyPrefix = "habitinator-undo";

    private readonly List<UndoAction> _undoStack = new();
    private readonly ISnackbar _snackbar;
    private readonly INotificationSettingsService _settingsService;
    private readonly INotificationSettingsRules _notificationRules;

    private List<Func<Task>>? _currentBatch;
    private string? _currentBatchDescription;
    private int _undoingCount;
    private readonly SemaphoreSlim _undoLock = new(1, 1);

    public bool IsUndoing => _undoingCount > 0;
    public bool CanUndo => _undoStack.Count > 0;
    public string? LastActionDescription => _undoStack.Count > 0 ? _undoStack[^1].Description : null;

    public event Action? OnStateChanged;
    public event Action? OnUndoPerformed;

    public UndoService(
        ISnackbar snackbar,
        INotificationSettingsService settingsService,
        INotificationSettingsRules notificationRules)
    {
        _snackbar = snackbar;
        _settingsService = settingsService;
        _notificationRules = notificationRules;
    }

    public Guid RegisterUndo(string description, Func<Task> undoFunc)
    {
        if (IsUndoing)
        {
            return Guid.Empty;
        }

        if (_currentBatch is not null)
        {
            _currentBatch.Add(undoFunc);
            return Guid.Empty;
        }

        var action = new UndoAction(description, undoFunc);
        _undoStack.Add(action);
        OnStateChanged?.Invoke();
        _ = ShowUndoSnackbarAsync(action);
        return action.Id;
    }

    public IDisposable BeginBatch(string description)
    {
        return new UndoBatch(this, description);
    }

    private void StartBatch(string description)
    {
        _currentBatch = new List<Func<Task>>();
        _currentBatchDescription = description;
    }

    private void EndBatch()
    {
        if (_currentBatch is null)
        {
            return;
        }

        var batch = _currentBatch;
        var desc = _currentBatchDescription ?? "Multiple actions";
        _currentBatch = null;
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
            });
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

        var index = actionId is null
            ? _undoStack.Count - 1
            : _undoStack.FindIndex(a => a.Id == actionId);

        if (index < 0)
        {
            return;
        }

        var action = _undoStack[index];
        _undoStack.RemoveAt(index);

        Interlocked.Increment(ref _undoingCount);
        await _undoLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await action.UndoFunc().ConfigureAwait(false);
            OnUndoPerformed?.Invoke();
        }
        catch (Exception)
        {
            // best-effort
        }
        finally
        {
            _undoLock.Release();
            Interlocked.Decrement(ref _undoingCount);
            DismissSnackbar(action);
            OnStateChanged?.Invoke();
        }
    }

    public void Clear()
    {
        _undoStack.Clear();
        OnStateChanged?.Invoke();
    }

    private async Task ShowUndoSnackbarAsync(UndoAction action)
    {
        try
        {
            var settings = await _settingsService.GetAsync(CancellationToken.None).ConfigureAwait(false);
            var ms = _notificationRules.UndoVisibleStateDurationMs(settings.ToastDuration);
            var key = $"{UndoSnackbarKeyPrefix}-{action.Id:N}";
            action.SnackbarKey = key;

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
                    config.DuplicatesBehavior = SnackbarDuplicatesBehavior.Allow;
                },
                key);
        }
        catch (Exception)
        {
            // best-effort
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
        catch (Exception)
        {
            // best-effort
        }
    }

    private sealed class UndoAction
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Description { get; }
        public Func<Task> UndoFunc { get; }
        public string? SnackbarKey { get; set; }

        public UndoAction(string description, Func<Task> undoFunc)
        {
            Description = description;
            UndoFunc = undoFunc;
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
