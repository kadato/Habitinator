using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace App.Shared.RCL.Components;

public partial class BoardItemCard
{
    [Inject] public required IUserTimeZoneService TimeZoneService { get; set; }
    [Inject] public required IUserDateFormatService DateFormatService { get; set; }

    [Parameter][EditorRequired] public required BoardItem Item { get; set; }
    [Parameter][EditorRequired] public BoardSection Section { get; set; }
    [Parameter] public bool CanReorder { get; set; }

    [Parameter] public EventCallback OnHabitUp { get; set; }
    [Parameter] public EventCallback OnHabitDown { get; set; }
    [Parameter] public EventCallback OnToggle { get; set; }
    [Parameter] public EventCallback OnOpenEditor { get; set; }
    [Parameter] public EventCallback OnMoveToTop { get; set; }
    [Parameter] public EventCallback OnMoveToBottom { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }
    [Parameter] public EventCallback<(Guid ChecklistItemId, bool IsDone)> OnSetChecklistItemDone { get; set; }

    private bool _subtasksExpanded = true;
    private BoardItem? _parsedForItem;
    private IReadOnlyList<DailyChecklistItem> _checklist = [];
    private List<string> _tagsList = [];

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_parsedForItem, Item))
        {
            return;
        }

        _parsedForItem = Item;
        _checklist = DailyChecklistJson.Parse(Item.ChecklistJson);
        _tagsList = [.. BoardTagUtil.ParseTags(Item.Tags)];
    }

    private IReadOnlyList<DailyChecklistItem> Checklist => _checklist;
    private int SubDone => Checklist.Count(x => x.IsDone);
    private int SubTotal => Checklist.Count;
    private List<string> TagsList => _tagsList;
    private bool HasBodyExtra => !string.IsNullOrWhiteSpace(Item.Notes) || SubTotal > 0 || TagsList.Count > 0;

    private string CardClass => $"board-card {RowClass}";
    private string RowClass => GetRowClass(Section, Item);
    private string CheckClass => GetCheckClass(Section, Item);

    private void ToggleSubtasksExpand()
    {
        _subtasksExpanded = !_subtasksExpanded;
    }

    private static string GetRowClass(BoardSection section, BoardItem item)
    {
        return section switch
        {
            BoardSection.Habit => "board-row--habit",
            BoardSection.Daily => item.IsCompleted ? "board-row--done" : "board-row--daily-open",
            BoardSection.Todo => item.IsCompleted ? "board-row--done" : "board-row--todo-open",
            _ => string.Empty
        };
    }

    private static string GetCheckClass(BoardSection section, BoardItem item)
    {
        if (item.IsCompleted)
        {
            return "board-check--ok";
        }

        return section == BoardSection.Daily ? "board-check--daily" : "board-check--todo";
    }

    private string TodoDueCssClass(DateOnly due)
    {
        var days = due.DayNumber - BoardToday().DayNumber;
        return days switch
        {
            < 0 => "board-todo-due board-todo-due--overdue",
            0 => "board-todo-due board-todo-due--today",
            _ => "board-todo-due board-todo-due--upcoming"
        };
    }

    private string TodoDueRelativeLabel(DateOnly due) =>
        TodoDueRelativeText.Format(due, BoardToday());

    private DateOnly BoardToday() => DailySchedule.LocalToday(TimeZoneService);

    private string HabitCounterTitle()
    {
        return (Item.TrackPlus, Item.TrackMinus) switch
        {
            (true, true) => "+ count and − count",
            (true, false) => "+ count only",
            (false, true) => "− count only",
            _ => "Counters"
        };
    }

    private static bool ShouldShowItemFooter(BoardSection section, BoardItem item)
    {
        return section switch
        {
            BoardSection.Todo => item.TodoDueDate is not null,
            BoardSection.Habit or BoardSection.Daily => true,
            _ => false
        };
    }

    private Dictionary<string, object> ToggleAria => new()
    {
        { "aria-pressed", Item.IsCompleted ? "true" : "false" },
        { "aria-checked", Item.IsCompleted ? "true" : "false" }
    };

    private Dictionary<string, object> SubtasksAria => new()
    {
        { "aria-expanded", _subtasksExpanded ? "true" : "false" },
        { "aria-label", $"Subtasks: {SubDone} completed out of {SubTotal}" }
    };

    private async Task OnTitleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ")
        {
            await OnOpenEditor.InvokeAsync();
        }
    }
}
