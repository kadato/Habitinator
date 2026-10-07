using App.Shared.RCL.Components.Dialogs;
using App.Shared.RCL.Models;

using MudBlazor;

namespace App.Shared.RCL.Components;

/// <summary>Opens the habit, daily, or to-do editor and routes archive and delete results.</summary>
internal sealed class BoardItemEditor(
    IDialogService dialogs,
    Func<BoardItem, Task> archive,
    Func<BoardItem, Task> delete)
{
    public Task OpenAsync(BoardSection section, BoardItem item) =>
        section switch
        {
            BoardSection.Habit => OpenEditHabitAsync(item),
            BoardSection.Daily => OpenEditDailyAsync(item),
            BoardSection.Todo => OpenEditTodoAsync(item),
            _ => Task.CompletedTask
        };

    private async Task OpenEditHabitAsync(BoardItem item)
    {
        DialogParameters<EditHabitDialog> parameters = new() { { x => x.Item, item } };
        var dialog = await dialogs.ShowAsync<EditHabitDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: EditHabitDialogResult r })
        {
            await HandleResultAsync(item, r.Action);
        }
    }

    private async Task OpenEditDailyAsync(BoardItem item)
    {
        DialogParameters<EditDailyDialog> parameters = new() { { x => x.Item, item } };
        var dialog = await dialogs.ShowAsync<EditDailyDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: EditDailyDialogResult r })
        {
            await HandleResultAsync(item, r.Action);
        }
    }

    private async Task OpenEditTodoAsync(BoardItem item)
    {
        DialogParameters<EditTodoDialog> parameters = new() { { x => x.Item, item } };
        var dialog = await dialogs.ShowAsync<EditTodoDialog>(string.Empty, parameters, DialogDefaults.SmallEditor);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: EditTodoDialogResult r })
        {
            await HandleResultAsync(item, r.Action);
        }
    }

    private Task HandleResultAsync(BoardItem item, EditDialogAction? action) =>
        action switch
        {
            EditDialogAction.Archive => archive(item),
            EditDialogAction.Delete => delete(item),
            _ => Task.CompletedTask
        };
}
