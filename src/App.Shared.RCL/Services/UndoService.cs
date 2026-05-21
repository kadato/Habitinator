using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using App.Shared.RCL.Models;
using MudBlazor;

namespace App.Shared.RCL.Services;

public sealed class UndoService : IUndoService
{
    private readonly Stack<UndoAction> _undoStack = new();
    private readonly ISnackbar _snackbar;
    private readonly INotificationSettingsService _settingsService;
    private readonly INotificationSettingsRules _notificationRules;

    private List<Func<Task>>? _currentBatch;
    private string? _currentBatchDescription;

    public bool IsUndoing { get; private set; }
    public bool CanUndo => _undoStack.Count > 0;
    public string? LastActionDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;

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

    public void RegisterUndo(string description, Func<Task> undoFunc)
    {
        if (IsUndoing) return;

        if (_currentBatch is not null)
        {
            _currentBatch.Add(undoFunc);
            return;
        }

        _undoStack.Push(new UndoAction(description, undoFunc));
        OnStateChanged?.Invoke();
        _ = ShowUndoSnackbarAsync(description);
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
        if (_currentBatch is null) return;

        var batch = _currentBatch;
        var desc = _currentBatchDescription ?? "Multiple actions";
        _currentBatch = null;
        _currentBatchDescription = null;

        if (batch.Count > 0)
        {
            batch.Reverse();
            RegisterUndo(desc, async () =>
            {
                foreach (var action in batch)
                {
                    await action();
                }
            });
        }
    }

    public async Task UndoAsync()
    {
        if (_undoStack.Count == 0 || IsUndoing) return;

        IsUndoing = true;
        var action = _undoStack.Pop();
        try
        {
            await action.UndoFunc();
            OnUndoPerformed?.Invoke();
        }
        catch (Exception)
        {
            // best-effort
        }
        finally
        {
            IsUndoing = false;
            OnStateChanged?.Invoke();
        }
    }

    public void Clear()
    {
        _undoStack.Clear();
        OnStateChanged?.Invoke();
    }

    private async Task ShowUndoSnackbarAsync(string description)
    {
        try
        {
            var settings = await _settingsService.GetAsync(CancellationToken.None).ConfigureAwait(false);
            var ms = _notificationRules.VisibleStateDurationMs(settings.ToastDuration);

            _snackbar.Add($"Action: {description}", Severity.Normal, config =>
            {
                config.VisibleStateDuration = ms;
                config.Action = "Undo";
                config.ActionColor = Color.Warning;
                config.Icon = Icons.Material.Filled.Undo;
                config.IconColor = Color.Warning;
                config.ShowCloseIcon = false;
                config.OnClick = async snackbar =>
                {
                    await UndoAsync().ConfigureAwait(false);
                };
            });
        }
        catch (Exception)
        {
            // best-effort
        }
    }

    private sealed class UndoAction
    {
        public string Description { get; }
        public Func<Task> UndoFunc { get; }

        public UndoAction(string description, Func<Task> undoFunc)
        {
            Description = description;
            UndoFunc = undoFunc;
        }
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
