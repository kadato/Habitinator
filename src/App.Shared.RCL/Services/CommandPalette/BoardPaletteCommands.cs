using App.Shared.RCL.Components;
using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;

using MudBlazor;

namespace App.Shared.RCL.Services.CommandPalette;

/// <summary>Board catalog fragments, search matches, sub-actions, and board mutations.</summary>
internal sealed class BoardPaletteCommands(
    IBoardDataService boardData,
    IUndoService undo,
    IDialogService dialogs,
    IUserNotifier notifier,
    Action<string, string, Guid?> startTimerOn,
    Action close,
    Func<Task> notifyRefresh)
{
    private readonly IBoardDataService _boardData = boardData;
    private readonly IUndoService _undo = undo;
    private readonly IDialogService _dialogs = dialogs;
    private readonly IUserNotifier _notifier = notifier;
    private readonly Action<string, string, Guid?> _startTimerOn = startTimerOn;
    private readonly Action _close = close;
    private readonly Func<Task> _notifyRefresh = notifyRefresh;

    private const string SuggestedCategory = "Suggested";
    private const string ActionsCategory = "Actions";
    private const string DeleteItemsCategory = "Delete Items";
    private const string StartTimerSubtitle = "Set timer target and start stopwatch";
    private const string MoveToArchiveSubtitle = "Move item to archive";
    private const string KwDelete = "delete";
    private const string KwRemove = "remove";
    private const string KwTrash = "trash";
    private const string KwDestroy = "destroy";
    private const string KwArchive = "archive";
    private const string KwHide = "hide";
    private const string KwAdd = "add";
    private const string KwDone = "done";
    private const string KwComplete = "complete";
    private const string KwCreate = "create";
    private const string KwNew = "new";
    private const string KwTodos = "todos";
    private const string KwTodoDash = "to-do";
    private const string KwTodoDashes = "to-dos";
    private const string KwTimer = "timer";
    private const string KwStart = "start";
    private const string KwFocus = "focus";
    private const string KwStopwatch = "stopwatch";
    private const string KwEdit = "edit";
    private const string KwRename = "rename";
    private const string KwChange = "change";
    private const string KwNotes = "notes";
    private const string KwChecklist = "checklist";

    public List<CommandItem> GetSuggestedCreateCommands() =>
    [
            new(
                Id: "create-todo",
                Title: "New to-do",
                Subtitle: "Add a single task to your board",
                Category: SuggestedCategory,
                Icon: Icons.Material.Filled.CheckBoxOutlineBlank,
                ShortcutBadge: "Alt+T",
                Action: () => CreateItemAsync(BoardSection.Todo),
                Keywords: ["todo", "task", KwCreate, KwNew, KwAdd]),

            new(
                Id: "create-habit",
                Title: "New habit",
                Subtitle: "Add a countable positive or negative habit",
                Category: SuggestedCategory,
                Icon: Icons.Material.Filled.Repeat,
                ShortcutBadge: "Ctrl+H",
                Action: () => CreateItemAsync(BoardSection.Habit),
                Keywords: ["habit", KwCreate, KwNew, KwAdd, "streak"]),

            new(
                Id: "create-daily",
                Title: "New daily",
                Subtitle: "Add a recurring daily habit",
                Category: SuggestedCategory,
                Icon: Icons.Material.Filled.CalendarToday,
                ShortcutBadge: "Ctrl+D",
                Action: () => CreateItemAsync(BoardSection.Daily),
                Keywords: ["daily", "recurring", "schedule", KwCreate, KwNew, KwAdd])
    ];

    public void AddBoardCleanupCommands(List<CommandItem> list)
    {
        list.Add(new(
            Id: "action-delete-completed-todos",
            Title: "Delete completed to-dos",
            Subtitle: "Permanently remove all completed to-do items",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.DeleteSweep,
            IsDanger: true,
            Action: DeleteCompletedTodosAsync,
            Keywords: [KwDelete, KwRemove, "clear", "completed", KwDone, KwTodos, "clean", KwTrash]));

        list.Add(new(
            Id: "action-archive-completed-todos",
            Title: "Archive completed to-dos",
            Subtitle: "Move all completed to-do tasks to archive",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Archive,
            Action: ArchiveCompletedTodosAsync,
            Keywords: [KwArchive, "completed", KwDone, KwTodos, KwHide]));

        list.Add(new(
            Id: "action-manage-items",
            Title: "Manage and delete items",
            Subtitle: "Browse active items to quickly delete, archive, or edit",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.DeleteOutline,
            ChildrenProvider: GetManageItemsSubActionsAsync,
            Keywords: [KwDelete, KwRemove, "manage", "items", "browse", "habits", "dailies", KwTodos, KwTrash]));
    }

    public void AddUndoCommand(List<CommandItem> list)
    {
        list.Add(new(
            Id: "action-undo",
            Title: "Undo last action",
            Subtitle: "Revert the most recent change",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Undo,
            ShortcutBadge: "Ctrl+Z",
            Action: UndoAsync,
            Keywords: ["undo", "revert"]));
    }

    public List<CommandItem>? TryBuildDeleteCommands(string q, BoardSnapshot snapshot)
    {
        var results = new List<CommandItem>();
        // Direct delete intent detection: "delete <item>", "del <item>", "remove <item>"
        var isDeleteIntent = false;
        var searchTarget = q;
        if (q.StartsWith("delete ", StringComparison.OrdinalIgnoreCase) ||
            q.StartsWith("remove ", StringComparison.OrdinalIgnoreCase))
        {
            isDeleteIntent = true;
            searchTarget = q[7..].Trim();
        }
        else if (q.StartsWith("del ", StringComparison.OrdinalIgnoreCase))
        {
            isDeleteIntent = true;
            searchTarget = q[4..].Trim();
        }

        if (isDeleteIntent && !string.IsNullOrWhiteSpace(searchTarget))
        {
            // Direct delete commands for matching items
            foreach (var h in snapshot.Habits.Where(h => Matches(h.Title, h.Notes, h.Tags, searchTarget)).Take(6))
            {
                results.Add(new CommandItem(
                    Id: $"direct-delete-habit-{h.Id}",
                    Title: $"Delete habit: {h.Title}",
                    Subtitle: "Permanently delete this habit",
                    Category: DeleteItemsCategory,
                    Icon: Icons.Material.Filled.Delete,
                    IsDanger: true,
                    Action: () => DeleteItemAsync(BoardSection.Habit, h.Id)));
            }

            foreach (var d in snapshot.Dailies.Where(d => Matches(d.Title, d.Notes, d.Tags, searchTarget)).Take(6))
            {
                results.Add(new CommandItem(
                    Id: $"direct-delete-daily-{d.Id}",
                    Title: $"Delete daily: {d.Title}",
                    Subtitle: "Permanently delete this daily",
                    Category: DeleteItemsCategory,
                    Icon: Icons.Material.Filled.Delete,
                    IsDanger: true,
                    Action: () => DeleteItemAsync(BoardSection.Daily, d.Id)));
            }

            foreach (var t in snapshot.Todos.Where(t => Matches(t.Title, t.Notes, t.Tags, searchTarget)).Take(6))
            {
                results.Add(new CommandItem(
                    Id: $"direct-delete-todo-{t.Id}",
                    Title: $"Delete to-do: {t.Title}",
                    Subtitle: "Permanently delete this to-do",
                    Category: DeleteItemsCategory,
                    Icon: Icons.Material.Filled.Delete,
                    IsDanger: true,
                    Action: () => DeleteItemAsync(BoardSection.Todo, t.Id)));
            }

            return results;
        }
        return null;
    }

    public void AddItemMatchCommands(string q, BoardSnapshot snapshot, List<CommandItem> results)
    {
        // Normal search: Habits
        var matchingHabits = snapshot.Habits
            .Where(h => Matches(h.Title, h.Notes, h.Tags, q))
            .Take(6);

        foreach (var h in matchingHabits)
        {
            results.Add(new CommandItem(
                Id: $"habit-{h.Id}",
                Title: h.Title,
                Subtitle: $"Habit, +{h.Counter}, -{h.NegativeCounter}",
                Category: "Habits",
                Icon: Icons.Material.Filled.Repeat,
                ChildrenProvider: () => Task.FromResult(GetHabitSubActions(h))));
        }

        // Dailies
        var matchingDailies = snapshot.Dailies
            .Where(d => Matches(d.Title, d.Notes, d.Tags, q))
            .Take(6);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var d in matchingDailies)
        {
            var isDone = d.DailyLastCompletedOn.HasValue &&
                         d.DailyLastCompletedOn.Value == today;
            results.Add(new CommandItem(
                Id: $"daily-{d.Id}",
                Title: d.Title,
                Subtitle: isDone ? $"Daily, completed today, streak: {d.Counter}" : $"Daily, streak: {d.Counter}",
                Category: "Dailies",
                Icon: isDone ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CalendarToday,
                ChildrenProvider: () => Task.FromResult(GetDailySubActions(d, isDone))));
        }

        // To-dos
        var matchingTodos = snapshot.Todos
            .Where(t => Matches(t.Title, t.Notes, t.Tags, q))
            .Take(6);

        foreach (var t in matchingTodos)
        {
            string status;
            if (t.IsCompleted)
            {
                status = "Completed";
            }
            else if (t.TodoDueDate.HasValue)
            {
                status = $"Due {t.TodoDueDate.Value:MMM d}";
            }
            else
            {
                status = "Open";
            }

            results.Add(new CommandItem(
                Id: $"todo-{t.Id}",
                Title: t.Title,
                Subtitle: $"To-do, {status}",
                Category: "To-dos",
                Icon: t.IsCompleted ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CheckBoxOutlineBlank,
                ChildrenProvider: () => Task.FromResult(GetTodoSubActions(t))));
        }
    }

    private List<CommandItem> GetHabitSubActions(BoardItem h) =>
    [
        new(
            Id: $"habit-{h.Id}-plus",
            Title: "+1 Increment count",
            Subtitle: "Log positive completion",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Add,
            Action: async () =>
            {
                _close();
                await _boardData.IncrementHabitPlusAsync(h.Id);
                await _notifyRefresh();
            },
            Keywords: ["plus", "increment", "+1", KwAdd]),
        new(
            Id: $"habit-{h.Id}-minus",
            Title: "-1 Decrement count",
            Subtitle: "Log setback count",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Remove,
            Action: async () =>
            {
                _close();
                await _boardData.IncrementHabitMinusAsync(h.Id);
                await _notifyRefresh();
            },
            Keywords: ["minus", "decrement", "-1", "subtract"]),
        new(
            Id: $"habit-{h.Id}-reset",
            Title: "Reset counters",
            Subtitle: "Set positive and negative counters back to 0",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.RestartAlt,
            Action: async () =>
            {
                _close();
                await _boardData.UpdateHabitAsync(h.Id, UpdateHabitArgs.From(h) with { Counter = 0, NegativeCounter = 0 });
                await _notifyRefresh();
                await _notifier.NotifyAsync($"Counters for '{h.Title}' reset to 0.", Severity.Info);
            },
            Keywords: ["reset", "clear", "zero"]),
        new(
            Id: $"habit-{h.Id}-timer",
            Title: "Start focus timer on this habit",
            Subtitle: StartTimerSubtitle,
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Timer,
            Action: () =>
            {
                _close();
                _startTimerOn("Habit", h.Title, h.Id);
                return Task.CompletedTask;
            },
            Keywords: [KwTimer, KwStart, KwFocus, KwStopwatch]),
        new(
            Id: $"habit-{h.Id}-edit",
            Title: "Edit habit details",
            Subtitle: "Open editor for title, notes, and checklist",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Edit,
            Action: async () =>
            {
                _close();
                var parameters = new DialogParameters<EditHabitDialog> { { x => x.Item, h } };
                var dialog = await _dialogs.ShowAsync<EditHabitDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
                await dialog.Result;
                await _notifyRefresh();
            },
            Keywords: [KwEdit, KwRename, KwChange, "modify", KwNotes, KwChecklist]),
        new(
            Id: $"habit-{h.Id}-archive",
            Title: "Archive habit",
            Subtitle: MoveToArchiveSubtitle,
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Archive,
            Action: async () =>
            {
                _close();
                await _boardData.ArchiveItemAsync(BoardSection.Habit, h.Id);
                await _notifyRefresh();
            },
            Keywords: [KwArchive, KwHide]),
        new(
            Id: $"habit-{h.Id}-delete",
            Title: "Delete habit",
            Subtitle: "Permanently delete this habit",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Delete,
            IsDanger: true,
            Action: () => DeleteItemAsync(BoardSection.Habit, h.Id),
            Keywords: [KwDelete, KwRemove, KwTrash, KwDestroy])
    ];

    private List<CommandItem> GetDailySubActions(BoardItem d, bool isDone)
    {
        var actions = new List<CommandItem>
        {
            new(
                Id: $"daily-{d.Id}-toggle",
                Title: isDone ? "Unmark completed" : "Mark completed today",
                Subtitle: isDone ? "Reopen this daily for today" : "Record daily streak progression",
                Category: ActionsCategory,
                Icon: isDone ? Icons.Material.Filled.RadioButtonUnchecked : Icons.Material.Filled.CheckCircle,
                Action: async () =>
                {
                    _close();
                    await _boardData.ToggleItemAsync(BoardSection.Daily, d.Id);
                    await _notifyRefresh();
                },
                Keywords: ["toggle", KwDone, KwComplete, "check"]),
            new(
                Id: $"daily-{d.Id}-yesterday",
                Title: "Mark completed for yesterday",
                Subtitle: "Backdate completion to yesterday",
                Category: ActionsCategory,
                Icon: Icons.Material.Filled.History,
                Action: async () =>
                {
                    _close();
                    var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                    await _boardData.CompleteDailyForDateAsync(d.Id, yesterday);
                    await _notifyRefresh();
                    await _notifier.NotifyAsync($"Marked '{d.Title}' completed for yesterday.", Severity.Success);
                },
                Keywords: ["yesterday", "retro", "backdate", KwComplete]),
            new(
                Id: $"daily-{d.Id}-heatmap",
                Title: "View completion heatmap",
                Subtitle: "Show historical completions and streak calendar",
                Category: ActionsCategory,
                Icon: Icons.Material.Filled.CalendarMonth,
                Action: async () =>
                {
                    _close();
                    var parameters = new DialogParameters<DailyHeatmapDialog>
                    {
                        { x => x.BoardItemId, d.Id },
                        { x => x.Title, d.Title }
                    };
                    await _dialogs.ShowAsync<DailyHeatmapDialog>(string.Empty, parameters, DialogDefaults.Wide);
                },
                Keywords: ["heatmap", "history", "streak", "calendar", "stats"]),
            new(
                Id: $"daily-{d.Id}-timer",
                Title: "Start focus timer on this daily",
                Subtitle: StartTimerSubtitle,
                Category: ActionsCategory,
                Icon: Icons.Material.Filled.Timer,
                Action: () =>
                {
                    _close();
                    _startTimerOn("Daily", d.Title, d.Id);
                    return Task.CompletedTask;
                },
                Keywords: [KwTimer, KwStart, KwFocus, KwStopwatch]),
            new(
                Id: $"daily-{d.Id}-edit",
                Title: "Edit daily details",
                Subtitle: "Open editor for schedule, checklist, and repeat settings",
                Category: ActionsCategory,
                Icon: Icons.Material.Filled.Edit,
                Action: async () =>
                {
                    _close();
                    var parameters = new DialogParameters<EditDailyDialog> { { x => x.Item, d } };
                    var dialog = await _dialogs.ShowAsync<EditDailyDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
                    await dialog.Result;
                    await _notifyRefresh();
                },
                Keywords: [KwEdit, KwRename, KwChange, "schedule", "repeat", KwNotes, KwChecklist]),
            new(
                Id: $"daily-{d.Id}-archive",
                Title: "Archive daily",
                Subtitle: MoveToArchiveSubtitle,
                Category: ActionsCategory,
                Icon: Icons.Material.Filled.Archive,
                Action: async () =>
                {
                    _close();
                    await _boardData.ArchiveItemAsync(BoardSection.Daily, d.Id);
                    await _notifyRefresh();
                },
                Keywords: [KwArchive, KwHide]),
            new(
                Id: $"daily-{d.Id}-delete",
                Title: "Delete daily",
                Subtitle: "Permanently delete this daily",
                Category: ActionsCategory,
                Icon: Icons.Material.Filled.Delete,
                IsDanger: true,
                Action: () => DeleteItemAsync(BoardSection.Daily, d.Id),
                Keywords: [KwDelete, KwRemove, KwTrash, KwDestroy])
        };

        return actions;
    }

    private List<CommandItem> GetTodoSubActions(BoardItem t)
    {
        var actions = new List<CommandItem>
        {
            new(
                Id: $"todo-{t.Id}-toggle",
                Title: t.IsCompleted ? "Mark as incomplete" : "Mark as done",
                Subtitle: t.IsCompleted ? "Reopen this to-do" : "Complete task",
                Category: ActionsCategory,
                Icon: t.IsCompleted ? Icons.Material.Filled.RadioButtonUnchecked : Icons.Material.Filled.CheckCircle,
                Action: async () =>
                {
                    _close();
                    await _boardData.ToggleItemAsync(BoardSection.Todo, t.Id);
                    await _notifyRefresh();
                },
                Keywords: ["toggle", KwDone, KwComplete, "finish", "reopen"]),
            new(
                Id: $"todo-{t.Id}-due-today",
                Title: "Set due date to today",
                Subtitle: "Set deadline to today",
                Category: ActionsCategory,
                Icon: Icons.Material.Filled.Today,
                Action: async () =>
                {
                    _close();
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    await _boardData.UpdateTodoAsync(t.Id, UpdateTodoArgs.From(t) with { DueDate = today });
                    await _notifyRefresh();
                    await _notifier.NotifyAsync($"Due date for '{t.Title}' set to today.", Severity.Success);
                },
                Keywords: ["due", "today", "deadline", "date"]),
            new(
                Id: $"todo-{t.Id}-due-tomorrow",
                Title: "Set due date to tomorrow",
                Subtitle: "Set deadline to tomorrow",
                Category: ActionsCategory,
                Icon: Icons.Material.Filled.Event,
                Action: async () =>
                {
                    _close();
                    var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
                    await _boardData.UpdateTodoAsync(t.Id, UpdateTodoArgs.From(t) with { DueDate = tomorrow });
                    await _notifyRefresh();
                    await _notifier.NotifyAsync($"Due date for '{t.Title}' set to tomorrow.", Severity.Success);
                },
                Keywords: ["due", "tomorrow", "deadline", "date"])
        };

        if (t.TodoDueDate.HasValue)
        {
            actions.Add(new(
                Id: $"todo-{t.Id}-clear-due",
                Title: "Clear due date",
                Subtitle: $"Remove deadline, currently {t.TodoDueDate.Value:MMM d}",
                Category: ActionsCategory,
                Icon: Icons.Material.Filled.EventBusy,
                Action: async () =>
                {
                    _close();
                    await _boardData.UpdateTodoAsync(t.Id, UpdateTodoArgs.From(t) with { DueDate = null });
                    await _notifyRefresh();
                    await _notifier.NotifyAsync($"Cleared due date for '{t.Title}'.", Severity.Info);
                },
                Keywords: ["clear due", "remove deadline", "no date"]));
        }

        actions.Add(new(
            Id: $"todo-{t.Id}-timer",
            Title: "Start focus timer on this to-do",
            Subtitle: StartTimerSubtitle,
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Timer,
            Action: () =>
            {
                _close();
                _startTimerOn("Todo", t.Title, t.Id);
                return Task.CompletedTask;
            },
            Keywords: [KwTimer, KwStart, KwFocus, KwStopwatch]));

        actions.Add(new(
            Id: $"todo-{t.Id}-edit",
            Title: "Edit to-do details",
            Subtitle: "Open editor for title, notes, and checklist",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Edit,
            Action: async () =>
            {
                _close();
                var parameters = new DialogParameters<EditTodoDialog> { { x => x.Item, t } };
                var dialog = await _dialogs.ShowAsync<EditTodoDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
                await dialog.Result;
                await _notifyRefresh();
            },
            Keywords: [KwEdit, KwRename, KwChange, "modify", KwNotes, KwChecklist]));

        actions.Add(new(
            Id: $"todo-{t.Id}-archive",
            Title: "Archive to-do",
            Subtitle: MoveToArchiveSubtitle,
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Archive,
            Action: async () =>
            {
                _close();
                await _boardData.ArchiveItemAsync(BoardSection.Todo, t.Id);
                await _notifyRefresh();
            },
            Keywords: [KwArchive, KwHide]));

        actions.Add(new(
            Id: $"todo-{t.Id}-delete",
            Title: "Delete to-do",
            Subtitle: "Permanently delete this to-do",
            Category: ActionsCategory,
            Icon: Icons.Material.Filled.Delete,
            IsDanger: true,
            Action: () => DeleteItemAsync(BoardSection.Todo, t.Id),
            Keywords: [KwDelete, KwRemove, KwTrash, KwDestroy]));

        return actions;
    }

    private static bool Matches(string title, string? notes, string? tags, string query)
    {
        if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (notes is not null && notes.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (tags is not null && tags.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
    private bool _isCreatingItem;

    public async Task CreateItemAsync(BoardSection section)
    {
        if (_isCreatingItem)
        {
            return;
        }

        _isCreatingItem = true;
        _close();
        try
        {
            var item = await _boardData.CreateItemAsync(section, GetDefaultTitle(section));
            await _notifyRefresh();
            if (item is not null)
            {
                await ShowCreateDialogAsync(section, item);
                await PruneUnchangedItemAsync(section, item);
                await _notifyRefresh();
            }
        }
        catch
        {
            await _notifier.NotifyAsync("Could not create item.", Severity.Error);
        }
        finally
        {
            _isCreatingItem = false;
        }
    }

    private static string GetDefaultTitle(BoardSection section) =>
        section switch
        {
            BoardSection.Habit => "New Habit",
            BoardSection.Daily => "New Daily",
            BoardSection.Todo => "New To-do",
            _ => "New Item"
        };

    private async Task ShowCreateDialogAsync(BoardSection section, BoardItem item)
    {
        var options = DialogDefaults.SmallEditor;
        var dialog = section switch
        {
            BoardSection.Habit => await _dialogs.ShowAsync<EditHabitDialog>(
                string.Empty, new DialogParameters<EditHabitDialog> { { x => x.Item, item } }, options),
            BoardSection.Daily => await _dialogs.ShowAsync<EditDailyDialog>(
                string.Empty, new DialogParameters<EditDailyDialog> { { x => x.Item, item } }, options),
            _ => await _dialogs.ShowAsync<EditTodoDialog>(
                string.Empty, new DialogParameters<EditTodoDialog> { { x => x.Item, item } }, options)
        };

        var result = await dialog.Result;
        await HandleCreateDialogResultAsync(section, item, result);
    }

    private async Task HandleCreateDialogResultAsync(BoardSection section, BoardItem item, DialogResult? result)
    {
        if (result is not { Canceled: false, Data: not null })
        {
            return;
        }

        EditDialogAction? action = result.Data switch
        {
            EditHabitDialogResult h => h.Action,
            EditDailyDialogResult d => d.Action,
            EditTodoDialogResult t => t.Action,
            _ => null
        };

        if (action == EditDialogAction.Archive)
        {
            await _boardData.ArchiveItemAsync(section, item.Id);
        }
        else if (action == EditDialogAction.Delete)
        {
            await _boardData.DeleteItemAsync(section, item.Id);
        }
    }

    private async Task PruneUnchangedItemAsync(BoardSection section, BoardItem item)
    {
        var current = await _boardData.GetItemAsync(item.Id);
        if (current is not null && current == item)
        {
            await _boardData.DeleteItemAsync(section, item.Id);
        }
    }

    public async Task UndoAsync()
    {
        _close();
        if (_undo.CanUndo)
        {
            await _undo.UndoAsync();
            await _notifyRefresh();
        }
        else
        {
            await _notifier.NotifyAsync("Nothing to undo.", Severity.Info);
        }
    }
    public async Task DeleteItemAsync(BoardSection section, Guid itemId)
    {
        _close();
        await TryNotifyAsync(async () =>
        {
            await _boardData.DeleteItemAsync(section, itemId);
            await _notifyRefresh();
        }, "Could not delete item.");
    }

    private async Task<bool> TryNotifyAsync(Func<Task> work, string errorMessage)
    {
        try
        {
            await work();
            return true;
        }
        catch
        {
            await _notifier.NotifyAsync(errorMessage, Severity.Error);
            return false;
        }
    }

    public async Task DeleteCompletedTodosAsync()
    {
        _close();
        await TryNotifyAsync(async () =>
        {
            var snapshot = await _boardData.GetSnapshotAsync();
            var completed = snapshot.Todos.Where(t => t.IsCompleted).ToList();
            if (completed.Count == 0)
            {
                await _notifier.NotifyAsync("No completed to-dos to delete.", Severity.Info);
                return;
            }

            var deleted = 0;
            using (_undo.BeginBatch($"Delete {completed.Count} done to-dos"))
            {
                foreach (var todo in completed)
                {
                    var success = await _boardData.DeleteItemAsync(BoardSection.Todo, todo.Id);
                    if (success)
                    {
                        deleted++;
                    }
                }
            }

            await _notifyRefresh();
            if (deleted < completed.Count)
            {
                var itemWord = completed.Count == 1 ? KwTodoDash : KwTodoDashes;
                await _notifier.NotifyAsync($"Removed {deleted} of {completed.Count} {itemWord}. Some could not be deleted.", Severity.Warning);
            }
            else
            {
                var itemWord = deleted == 1 ? KwTodoDash : KwTodoDashes;
                await _notifier.NotifyAsync($"Deleted {deleted} completed {itemWord}.", Severity.Success);
            }
        }, "Could not delete completed to-dos.");
    }

    public async Task ArchiveCompletedTodosAsync()
    {
        _close();
        await TryNotifyAsync(async () =>
        {
            var snapshot = await _boardData.GetSnapshotAsync();
            var completed = snapshot.Todos.Where(t => t.IsCompleted).ToList();
            if (completed.Count == 0)
            {
                await _notifier.NotifyAsync("No completed to-dos to archive.", Severity.Info);
                return;
            }

            var archived = 0;
            using (_undo.BeginBatch($"Archive {completed.Count} done to-dos"))
            {
                foreach (var todo in completed)
                {
                    var result = await _boardData.ArchiveItemAsync(BoardSection.Todo, todo.Id);
                    if (result is not null)
                    {
                        archived++;
                    }
                }
            }

            await _notifyRefresh();
            if (archived < completed.Count)
            {
                var itemWord = completed.Count == 1 ? KwTodoDash : KwTodoDashes;
                await _notifier.NotifyAsync($"Archived {archived} of {completed.Count} {itemWord}. Some could not be archived.", Severity.Warning);
            }
            else
            {
                var itemWord = archived == 1 ? KwTodoDash : KwTodoDashes;
                await _notifier.NotifyAsync($"Archived {archived} completed {itemWord}.", Severity.Success);
            }
        }, "Could not archive completed to-dos.");
    }
    private async Task<List<CommandItem>> GetManageItemsSubActionsAsync()
    {
        var items = new List<CommandItem>();
        try
        {
            var snapshot = await _boardData.GetSnapshotAsync();

            foreach (var h in snapshot.Habits)
            {
                items.Add(new CommandItem(
                    Id: $"manage-habit-{h.Id}",
                    Title: h.Title,
                    Subtitle: $"Habit, +{h.Counter}, -{h.NegativeCounter}",
                    Category: "Habits",
                    Icon: Icons.Material.Filled.Repeat,
                    ChildrenProvider: () => Task.FromResult(GetHabitSubActions(h))));
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var d in snapshot.Dailies)
            {
                var isDone = d.DailyLastCompletedOn.HasValue && d.DailyLastCompletedOn.Value == today;
                items.Add(new CommandItem(
                    Id: $"manage-daily-{d.Id}",
                    Title: d.Title,
                    Subtitle: isDone ? $"Daily, completed today, streak: {d.Counter}" : $"Daily, streak: {d.Counter}",
                    Category: "Dailies",
                    Icon: isDone ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CalendarToday,
                    ChildrenProvider: () => Task.FromResult(GetDailySubActions(d, isDone))));
            }

            foreach (var t in snapshot.Todos)
            {
                string status;
                if (t.IsCompleted)
                {
                    status = "Completed";
                }
                else if (t.TodoDueDate.HasValue)
                {
                    status = $"Due {t.TodoDueDate.Value:MMM d}";
                }
                else
                {
                    status = "Open";
                }

                items.Add(new CommandItem(
                    Id: $"manage-todo-{t.Id}",
                    Title: t.Title,
                    Subtitle: $"To-do, {status}",
                    Category: "To-dos",
                    Icon: t.IsCompleted ? Icons.Material.Filled.CheckCircle : Icons.Material.Filled.CheckBoxOutlineBlank,
                    ChildrenProvider: () => Task.FromResult(GetTodoSubActions(t))));
            }
        }
        catch
        {
            // Best effort.
        }

        return items;
    }
}
