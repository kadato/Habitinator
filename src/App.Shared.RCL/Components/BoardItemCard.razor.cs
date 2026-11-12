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
    [Parameter] public EventCallback OnMoveUp { get; set; }
    [Parameter] public EventCallback OnMoveDown { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }
    [Parameter] public EventCallback<(Guid ChecklistItemId, bool IsDone)> OnSetChecklistItemDone { get; set; }

    /// <summary>True while a habit log or toggle is saving. Shows the pending state. Blocks double-tap.</summary>
    [Parameter] public bool IsBusy { get; set; }

    /// <summary>Oldest missed scheduled day. Set for dailies with past misses in the catch-up window.</summary>
    [Parameter] public DateOnly? OverdueSince { get; set; }

    private bool _subtasksExpanded = true;
    private bool _tagsExpanded;
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
    private static string AriaBool(bool condition) => condition ? "true" : "false";
    private BoardItem? _lastRenderedItem;
    private BoardSection? _lastRenderedSection;
    private bool? _lastRenderedCanReorder;
    private bool? _lastRenderedSubtasksExpanded;
    private bool? _lastRenderedTagsExpanded;
    private DateOnly? _lastRenderedOverdueSince;

    protected override bool ShouldRender()
    {
        return !Equals(_lastRenderedItem, Item)
               || _lastRenderedSection != Section
               || _lastRenderedCanReorder != CanReorder
               || _lastRenderedSubtasksExpanded != _subtasksExpanded
               || _lastRenderedTagsExpanded != _tagsExpanded
               || _lastRenderedOverdueSince != OverdueSince
               || _lastRenderedIsBusy != IsBusy;
    }

    private bool? _lastRenderedIsBusy;

    protected override void OnAfterRender(bool firstRender)
    {
        _lastRenderedItem = Item;
        _lastRenderedSection = Section;
        _lastRenderedCanReorder = CanReorder;
        _lastRenderedSubtasksExpanded = _subtasksExpanded;
        _lastRenderedTagsExpanded = _tagsExpanded;
        _lastRenderedOverdueSince = OverdueSince;
        _lastRenderedIsBusy = IsBusy;
    }

    private void ToggleSubtasksExpand()
    {
        _subtasksExpanded = !_subtasksExpanded;
    }

    private void ToggleTagsExpand()
    {
        _tagsExpanded = !_tagsExpanded;
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

    /// <summary>
    ///     Deterministic accent color per tag so the same tag always gets the same hue. The lightness is
    ///     theme-aware: darker in light mode, brighter in dark mode, so the text stays readable on both.
    /// </summary>
    private static string TagBadgeStyle(string tag)
    {
        var hue = StableHash(tag) % 360;
        return $"--tag-color: light-dark(hsl({hue} 60% 38%), hsl({hue} 75% 72%))";
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in value)
            {
                hash = hash * 31 + c;
            }

            return hash;
        }
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

    private string ResetPeriodLabel => Item.ResetPeriod switch
    {
        HabitResetPeriod.Weekly => "Weekly",
        HabitResetPeriod.Monthly => "Monthly",
        _ => string.Empty
    };

    private string HabitCounterTitle()
    {
        return (Item.TrackPlus, Item.TrackMinus) switch
        {
            (true, true) => $"Positive logs {Item.Counter} and negative logs {Item.NegativeCounter}",
            (true, false) => $"Positive logs {Item.Counter}",
            (false, true) => $"Negative logs {Item.NegativeCounter}",
            _ => "Counters"
        };
    }

    private bool ShouldShowItemFooter(BoardSection section, BoardItem item)
    {
        return section switch
        {
            BoardSection.Todo => item.TodoDueDate is not null,
            BoardSection.Habit => item.TrackPlus || item.TrackMinus || item.ResetPeriod != HabitResetPeriod.Daily,
            BoardSection.Daily => OverdueSince is not null,
            _ => false
        };
    }

    private async Task OnTitleKeyDown(KeyboardEventArgs e)
    {
        // Native <button> already activates on Enter or Space. Only handle habit shortcuts here.
        if (Section != BoardSection.Habit || IsBusy)
        {
            return;
        }

        if ((e.Key is "+" or "=") && Item.TrackPlus)
        {
            await OnHabitUp.InvokeAsync();
        }
        else if ((e.Key is "-" or "_" or "Subtract") && Item.TrackMinus)
        {
            await OnHabitDown.InvokeAsync();
        }
    }

    private string GetTitleAriaLabel()
    {
        if (Section == BoardSection.Habit)
        {
            if (Item.TrackPlus && Item.TrackMinus)
            {
                return $"Open details for {Item.Title}. Press plus to log positive. Press minus to log negative.";
            }
            if (Item.TrackPlus)
            {
                return $"Open details for {Item.Title}. Press plus to log positive.";
            }
            if (Item.TrackMinus)
            {
                return $"Open details for {Item.Title}. Press minus to log negative.";
            }
        }

        return $"Open details for {Item.Title}";
    }

    private string? GetTitleKeyShortcuts()
    {
        if (Section != BoardSection.Habit)
        {
            return null;
        }

        return (Item.TrackPlus, Item.TrackMinus) switch
        {
            (true, true) => "+ -",
            (true, false) => "+",
            (false, true) => "-",
            _ => null
        };
    }
}
