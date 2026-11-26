#pragma warning disable S8949, S6966, IDE0028, IDE0042, CA1859
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class BoardColumn
{
    private string _draft = string.Empty;
    private bool _draftError;
    private bool _isAdding;
    private string? _duplicateExistingTitle;
    private bool _duplicateConfirmed;
    private BoardItemEditor? _editor;

    private void ClearDraftError()
    {
        _draftError = false;
        _duplicateExistingTitle = null;
        _duplicateConfirmed = false;
    }

    private string GetAddHint() => Section == BoardSection.Todo
        ? "Use #health for a tag and due:tomorrow for a date."
        : "Use #health for a tag.";

    private async Task SetChecklistItemDoneAsync(BoardItem item, Guid lineId, bool done)
    {
        var today = BoardToday();
        var rows = DailyChecklistJson.Parse(item.ChecklistJson);
        List<DailyChecklistItem> list = [.. rows];
        var i = list.FindIndex(x => x.Id == lineId);
        if (i < 0 || list[i].IsDone == done)
        {
            return;
        }

        // A checked daily subtask stores the local day in DoneOn. BoardItemMapper uses that
        // day to decide whether the check belongs to the current cycle.
        var doneOn = list[i].DoneOn;
        if (Section == BoardSection.Daily)
        {
            doneOn = done ? today : null;
        }

        list[i] = list[i] with { IsDone = done, DoneOn = doneOn };
        var json = DailyChecklistJson.Serialize(list);
        if (json is null)
        {
            return;
        }

        var optimistic = item with { ChecklistJson = json };
        Task mutation;
        if (Section == BoardSection.Daily)
        {
            mutation = BoardData.UpdateDailyAsync(
                item.Id,
                UpdateDailyArgs.From(item) with { ChecklistJson = json });
        }
        else if (Section == BoardSection.Todo)
        {
            mutation = BoardData.UpdateTodoAsync(
                item.Id,
                UpdateTodoArgs.From(item) with { ChecklistJson = json });
        }
        else
        {
            mutation = BoardData.UpdateHabitAsync(
                item.Id,
                UpdateHabitArgs.From(item) with { ChecklistJson = json });
        }

        await ApplyOverrideAsync(item.Id, optimistic, () => mutation);
    }

    private async Task OnDraftKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key is not "Enter" and not "NumpadEnter")
        {
            return;
        }

        await AddFromDraftAsync();
    }

    private async Task AddFromDraftAsync()
    {
        if (_isAdding)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_draft))
        {
            _draftError = true;
            return;
        }

        _isAdding = true;
        try
        {
            _draftError = false;
            var rawTitle = _draft.Trim();
            var (title, tags, dueDate) = BoardQuickAddParser.ParseInlineTitle(rawTitle, BoardToday());
            if (!_duplicateConfirmed)
            {
                var existing = Items.FirstOrDefault(i => BoardTitleSimilarity.IsSimilar(i.Title, title));
                if (existing is not null)
                {
                    _duplicateExistingTitle = existing.Title;
                    return;
                }
            }

            _duplicateExistingTitle = null;
            _duplicateConfirmed = false;
            var newId = Guid.NewGuid();
            var todoDueDate = Section == BoardSection.Todo ? dueDate : null;
            BoardItem tempItem = new(
                Id: newId,
                Title: title,
                Tags: tags,
                TodoDueDate: todoDueDate,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                SortOrder: GetInitialSortOrderForOptimisticCreation()
            );
            _draft = string.Empty;

            using var undoBatch = UndoService.BeginBatch($"Added \"{title}\"");
            await ApplyCreationAsync(newId, tempItem, async () =>
            {
                var created = await BoardData.CreateItemAsync(Section, title, newId);
                if (tags is null && todoDueDate is null)
                {
                    return;
                }

                try
                {
                    if (Section == BoardSection.Habit)
                    {
                        await BoardData.UpdateHabitAsync(created.Id, UpdateHabitArgs.From(created) with { Tags = tags ?? created.Tags });
                    }
                    else if (Section == BoardSection.Daily)
                    {
                        await BoardData.UpdateDailyAsync(created.Id, UpdateDailyArgs.From(created) with { Tags = tags ?? created.Tags });
                    }
                    else
                    {
                        await BoardData.UpdateTodoAsync(created.Id, UpdateTodoArgs.From(created) with
                        {
                            Tags = tags ?? created.Tags,
                            DueDate = todoDueDate ?? created.TodoDueDate
                        });
                    }
                }
                catch
                {
                    // The item exists. Tag and due-date enrichment is best effort.
                }
            });
        }
        finally
        {
            _isAdding = false;
        }
    }

    private async Task CreateDuplicateAnywayAsync()
    {
        _duplicateConfirmed = true;
        _duplicateExistingTitle = null;
        await AddFromDraftAsync();
    }

    private void CancelDuplicate()
    {
        _duplicateExistingTitle = null;
        _duplicateConfirmed = false;
    }

    private async Task StartFocusAsync(BoardItem item)
    {
        if (TimerService.IsRunning)
        {
            await Notifier.NotifyAsync("A focus session is already running.", Severity.Info);
            return;
        }

        TimerService.SelectTarget(Section.ToString(), item.Title, item.Id);
        TimerService.Start();
        await Notifier.NotifyAsync($"Focusing on '{item.Title}'.", Severity.Success);
    }

    private async Task SkipTodayAsync(BoardItem item)
    {
        try
        {
            var updated = await BoardData.SkipDailyForDateAsync(item.Id, BoardToday());
            if (updated is null)
            {
                await Notifier.NotifyAsync("This daily can't be skipped.", Severity.Error);
                return;
            }

            await Notifier.NotifyAsync($"Skipped '{item.Title}'. The streak is safe.", Severity.Success);
            await OnChanged.InvokeAsync();
        }
        catch
        {
            await Notifier.NotifyAsync("Couldn't skip this daily. Try again.", Severity.Error);
        }
    }

    private Task HabitUpAsync(BoardItem item)
    {
        var optimistic = item with { Counter = item.Counter + 1 };
        return ApplyOverrideAsync(item.Id, optimistic, () => BoardData.IncrementHabitPlusAsync(item.Id));
    }

    private Task HabitDownAsync(BoardItem item)
    {
        var optimistic = item with { NegativeCounter = item.NegativeCounter + 1 };
        return ApplyOverrideAsync(item.Id, optimistic, () => BoardData.IncrementHabitMinusAsync(item.Id));
    }

    private Task ToggleAsync(BoardItem item)
    {
        var today = BoardToday();
        var (lastCompleted, isCompleted) = DailySchedule.ToggleForToday(
            item.DailyLastCompletedOn, item.IsCompleted, today);
        var optimistic = item with
        {
            IsCompleted = isCompleted,
            DailyLastCompletedOn = lastCompleted,
            DailyPostponedUntil = null,
            DailyPostponedFrom = null,
            DailySkippedOn = null
        };
        return ApplyOverrideAsync(item.Id, optimistic, () => BoardData.ToggleItemAsync(Section, item.Id));
    }

    private async Task PostponeDailyAsync(BoardItem item)
    {
        var today = BoardToday();
        var tomorrow = today.AddDays(1);
        var from = item.DailyPostponedUntil == today
            ? item.DailyPostponedFrom ?? today.AddDays(-1)
            : today;
        var optimistic = item with
        {
            DailyPostponedUntil = tomorrow,
            DailyPostponedFrom = from,
            IsCompleted = false
        };
        if (await ApplyOverrideAsync(item.Id, optimistic, () => BoardData.PostponeDailyAsync(item.Id, tomorrow)))
        {
            await Notifier.NotifyAsync($"Delayed '{item.Title}' to tomorrow.", Severity.Success);
        }
    }

    private Task DelayTodoAsync(BoardItem item)
    {
        var tomorrow = BoardToday().AddDays(1);
        var optimistic = item with { TodoDueDate = tomorrow };
        return ApplyOverrideAsync(
            item.Id,
            optimistic,
            () => BoardData.UpdateTodoAsync(item.Id, UpdateTodoArgs.From(item) with { DueDate = tomorrow }));
    }

    private Task OpenItemEditorAsync(BoardItem item)
    {
        _editor ??= new BoardItemEditor(DialogService, ArchiveItemAsync, DeleteItemAsync);
        return _editor.OpenAsync(Section, item);
    }

    private Task ArchiveItemAsync(BoardItem item) =>
        ApplyDeletionAsync(item.Id, () => BoardData.ArchiveItemAsync(Section, item.Id), item.Title);

    private Task DeleteItemAsync(BoardItem item) => DeleteAsync(item.Id, item.Title);

    private Task DeleteAsync(Guid id, string? title = null) =>
        ApplyDeletionAsync(id, () => BoardData.DeleteItemAsync(Section, id), title);

    private async Task DeleteAllDoneTodosAsync()
    {
        if (Section != BoardSection.Todo)
        {
            return;
        }

        List<BoardItem> toDelete = [.. EffectiveItems.Where(x => x.IsCompleted)];
        if (toDelete.Count == 0)
        {
            return;
        }

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Delete all done to-dos?",
            $"This will permanently remove {toDelete.Count} {(toDelete.Count == 1 ? "done to-do" : "done to-dos")}. This cannot be undone.",
            "Delete all",
            null,
            "Cancel");

        if (confirmed != true)
        {
            return;
        }

        var deleted = 0;
        try
        {
            using (UndoService.BeginBatch($"Delete {toDelete.Count} done to-dos"))
            {
                foreach (var item in toDelete)
                {
                    if (await BoardData.DeleteItemAsync(BoardSection.Todo, item.Id))
                    {
                        deleted++;
                    }
                }
            }

            await OnChanged.InvokeAsync();
            // Full success is covered by the batch undo toast for "Delete N done to-dos".
            if (deleted < toDelete.Count)
            {
                await Notifier.NotifyAsync(
                    $"Removed {deleted} of {toDelete.Count} to-dos. Some to-dos stayed. Try again.",
                    Severity.Warning);
            }
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Couldn't delete. Try again.", Severity.Error);
        }
    }
}
