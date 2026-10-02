using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services;

/// <summary>
/// Validates an export file before restore. Strings, enums, sections, duplicates,
/// and sizes fail the file. Numbers the server already clamps on write are left
/// for the import service to clamp the same way.
/// </summary>
public static class UserDataImportValidator
{
    public const int MaxItems = 2000;
    public const int MaxEvents = 50000;

    /// <returns>First problem found, or null when the file restores cleanly.</returns>
    public static string? Validate(UserDataExportDto? data)
    {
        if (data is null)
        {
            return "The file is empty or is not a Habitinator export.";
        }

        if (data.Items is null)
        {
            return "The file has no items list. Export again from Settings.";
        }

        if (data.Items.Count > MaxItems)
        {
            return $"The file holds {data.Items.Count} items, above the {MaxItems} limit.";
        }

        if (data.Events is null)
        {
            return "The file has no activity list. Export again from Settings.";
        }

        if (data.Events.Count > MaxEvents)
        {
            return $"The file holds {data.Events.Count} activity events, above the {MaxEvents} limit.";
        }

        var ids = new HashSet<Guid>();
        for (var i = 0; i < data.Items.Count; i++)
        {
            var entry = data.Items[i];
            if (entry?.Item is null)
            {
                return $"Item {i + 1} is missing data. Export again from Settings.";
            }

            if (!ids.Add(entry.Item.Id))
            {
                return $"Item {i + 1} reuses an id already in the file.";
            }

            if (ValidateItem(entry, i) is { } itemError)
            {
                return itemError;
            }
        }

        for (var i = 0; i < data.Events.Count; i++)
        {
            if (ValidateEvent(data.Events[i], i) is { } eventError)
            {
                return eventError;
            }
        }

        return null;
    }

    private static string? ValidateItem(BoardSyncItem entry, int index)
    {
        var n = index + 1;
        var item = entry.Item;
        if (!Enum.IsDefined(entry.Section))
        {
            return $"Item {n} has an unknown section.";
        }

        var title = item.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return $"Item {n} has no title.";
        }

        if (title.Length > 200)
        {
            return $"Item {n} has a title above the 200 character limit.";
        }

        if (item.Notes is { Length: > 4000 })
        {
            return $"Item {n} has notes above the 4000 character limit.";
        }

        if (item.Tags is { Length: > 500 })
        {
            return $"Item {n} has tags above the 500 character limit.";
        }

        if (item.ChecklistJson is { Length: > 8000 })
        {
            return $"Item {n} has a checklist above the 8000 character limit.";
        }

        if (!Enum.IsDefined(item.DailyRepeat))
        {
            return $"Item {n} has an unknown repeat type.";
        }

        if (!Enum.IsDefined(item.ResetPeriod))
        {
            return $"Item {n} has an unknown reset period.";
        }

        return null;
    }

    private static string? ValidateEvent(UserActivityEventRecord record, int index)
    {
        var n = index + 1;
        if (!Enum.IsDefined(record.EventType))
        {
            return $"Activity {n} has an unknown event type.";
        }

        if (record.OccurredAtUtc == default)
        {
            return $"Activity {n} has no timestamp.";
        }

        if (record.DurationSeconds is < 0)
        {
            return $"Activity {n} has a negative duration.";
        }

        return null;
    }
}
