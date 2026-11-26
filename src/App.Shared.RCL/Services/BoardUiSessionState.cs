using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

public sealed class BoardUiSessionState
{
    public string SearchText { get; set; } = string.Empty;
#pragma warning disable IDE0028
    public HashSet<string> SelectedFilterTags { get; } = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028
    public bool RequireAllFilterTags { get; set; }
    public BoardQuickFilter QuickFilter { get; set; } = BoardQuickFilter.All;
    public int MobileSectionIndex { get; set; }
    public bool MobileSectionChosen { get; set; }
    public int SettingsSectionIndex { get; set; }
    public bool IsAiDrawerOpen { get; set; }

    /// <summary>Queued prompt for the AI drawer. Set by palette quick actions, consumed on open.</summary>
    public string? PendingAiPrompt { get; set; }

    public event Action? OnAiDrawerChanged;

    public void ToggleAiDrawer()
    {
        IsAiDrawerOpen = !IsAiDrawerOpen;
        OnAiDrawerChanged?.Invoke();
    }

    public void SetAiDrawer(bool open)
    {
        if (IsAiDrawerOpen != open)
        {
            IsAiDrawerOpen = open;
            OnAiDrawerChanged?.Invoke();
        }
    }
}
