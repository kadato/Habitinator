using App.Shared.RCL.Models;

namespace App.Shared.RCL.Components.Dialogs;

public static class EditDialogHelpers
{
    public static string FormatTags(string? rawTags)
    {
        return string.Join(", ", BoardTagUtil.ParseTags(rawTags));
    }

    public static List<ChecklistRow> ParseChecklistRows(string? checklistJson)
    {
        return DailyChecklistJson.Parse(checklistJson)
            .Select(x => new ChecklistRow { Id = x.Id, Text = x.Text, IsDone = x.IsDone })
            .ToList();
    }

    public static string? SerializeChecklistRows(IReadOnlyList<ChecklistRow> rows)
    {
        return DailyChecklistJson.Serialize(ChecklistRow.ToChecklistModel(rows));
    }

    public static string? CleanStringPayload(string? input)
    {
        return string.IsNullOrWhiteSpace(input) ? null : input.Trim();
    }
}
