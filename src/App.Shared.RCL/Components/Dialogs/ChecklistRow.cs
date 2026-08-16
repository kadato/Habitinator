using App.Shared.RCL.Models;

namespace App.Shared.RCL.Components.Dialogs;

public sealed class ChecklistRow
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }

    public static IReadOnlyList<DailyChecklistItem> ToChecklistModel(IReadOnlyList<ChecklistRow> rows) =>
        rows.Where(r => !string.IsNullOrWhiteSpace(r.Text))
            .Select(r => new DailyChecklistItem(
                r.Id == Guid.Empty ? Guid.NewGuid() : r.Id,
                r.Text.Trim(),
                r.IsDone))
            .ToList();
}
