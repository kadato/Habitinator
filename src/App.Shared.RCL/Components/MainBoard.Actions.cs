#pragma warning disable S4487, S2325, S3459, S8949, S6966 // Razor markup reads and assigns the flagged fields through @if bindings and @ref. The flagged methods call injected services and ComponentBase members. Sonar analyzes this code-behind file in isolation.
using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using MudBlazor;

namespace App.Shared.RCL.Components;

public partial class MainBoard
{
    private void HandleUndoStateChanged(object? sender, EventArgs e)
    {
        var canUndo = UndoService.CanUndo;
        if (canUndo == _lastCanUndo)
        {
            return;
        }
        _lastCanUndo = canUndo;
        _ = InvokeAsync(StateHasChanged);
    }

    private bool? _lastCanUndo;

    private void HandleUndoPerformed(object? sender, EventArgs e)
    {
        _ = InvokeAsync(async () =>
        {
            _lastLocalMutationTime = DateTimeOffset.UtcNow;
            await LoadBoardAsync();
            await RefreshStreaksAsync();
            StateHasChanged();
        });
    }

    private async Task HandleUndoAsync()
    {
        await UndoService.UndoAsync();
    }

    [JSInvokable]
    public Task OnSwipeSectionAsync(int direction)
    {
        var next = direction > 0
            ? (_mobileSectionIndex + 1) % 3
            : (_mobileSectionIndex + 2) % 3;
        SetMobileSectionIndex(next);
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OnCtrlZPressed()
    {
        if (UndoService.CanUndo)
        {
            await HandleUndoAsync();
        }
    }

    private bool _isCreatingItem;

    public async Task CreateItemPublicAsync(BoardSection section)
    {
        if (_isCreatingItem)
        {
            return;
        }

        _isCreatingItem = true;
        var defaultTitle = section switch
        {
            BoardSection.Habit => "New Habit",
            BoardSection.Daily => "New Daily",
            BoardSection.Todo => "New To-do",
            _ => "New Item"
        };
        try
        {
            var item = await BoardDataService.CreateItemAsync(section, defaultTitle);
            if (item is not null)
            {
                await OnBoardChanged();
                var result = await OpenItemEditorForCreatedAsync(section, item);
                if (result is { Canceled: false, Data: not null })
                {
                    await HandleCreatedItemDialogActionAsync(section, item.Id, result.Data);
                }

                // The dialog autosaves every change, so an item that is still identical to the
                // freshly created one was never edited. Drop it to avoid cluttering the board.
                var current = await BoardDataService.GetItemAsync(item.Id);
                if (current is not null && current == item)
                {
                    await BoardDataService.DeleteItemAsync(section, item.Id);
                }

                await OnBoardChanged();
            }
        }
        catch (Exception)
        {
            await Notifier.NotifyAsync("Couldn't add item. Try again.", Severity.Error);
        }
        finally
        {
            _isCreatingItem = false;
        }
    }

    private async Task HandleCreatedItemDialogActionAsync(BoardSection section, Guid itemId, object data)
    {
        EditDialogAction? action = data switch
        {
            EditHabitDialogResult h => h.Action,
            EditDailyDialogResult d => d.Action,
            EditTodoDialogResult t => t.Action,
            _ => null
        };

        if (action == EditDialogAction.Archive)
        {
            await BoardDataService.ArchiveItemAsync(section, itemId);
        }
        else if (action == EditDialogAction.Delete)
        {
            await BoardDataService.DeleteItemAsync(section, itemId);
        }
    }

    private async Task<DialogResult?> OpenItemEditorForCreatedAsync(BoardSection section, BoardItem item)
    {
        var options = DialogDefaults.SmallEditor;

        var dialog = section switch
        {
            BoardSection.Habit => await DialogService.ShowAsync<EditHabitDialog>(
                string.Empty, new DialogParameters<EditHabitDialog> { { x => x.Item, item } }, options),
            BoardSection.Daily => await DialogService.ShowAsync<EditDailyDialog>(
                string.Empty, new DialogParameters<EditDailyDialog> { { x => x.Item, item } }, options),
            _ => await DialogService.ShowAsync<EditTodoDialog>(
                string.Empty, new DialogParameters<EditTodoDialog> { { x => x.Item, item } }, options)
        };

        return await dialog.Result;
    }

    private async Task OpenArchivedItemsDialogAsync()
    {
        try
        {
            if (_tagsMenu is not null)
            {
                await _tagsMenu.CloseMenuAsync();
            }
        }
        catch (Exception ex)
        {
            // Ignore close failures. The tags menu may already be closed. The handler logs the error and opens the archive dialog.
            System.Diagnostics.Debug.WriteLine(ex.Message);
        }

        var dialog = await DialogService.ShowAsync<ArchivedItemsDialog>(string.Empty, DialogDefaults.Wide);
        await dialog.Result;

        _lastLocalMutationTime = DateTimeOffset.UtcNow;
        await LoadBoardAsync();
    }
}
