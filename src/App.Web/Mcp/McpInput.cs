using System.Globalization;

using App.Shared.RCL.Models;

using ModelContextProtocol;

namespace App.Web.Mcp;

internal static class McpInput
{
    public const int MaxTitleLength = 200;
    public const int MaxNotesLength = 4000;
    public const int MaxTagsLength = 500;
    public const int MaxChecklistLength = 8000;
    public const int MaxCustomLabelLength = 500;
    public const int MaxDisplayNameLength = 100;
    public const int MaxDateFormatLength = 20;
    public const int MaxTimeZoneLength = 100;
    public const int MaxImportJsonLength = 10_000_000;

    public static string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitleLength)
        {
            throw new McpProtocolException("Title must be 1 to 200 chars.", McpErrorCode.InvalidParams);
        }

        return title.Trim();
    }

    public static void CheckNotes(string? notes)
    {
        if (notes is not null && notes.Length > MaxNotesLength)
        {
            throw new McpProtocolException("Notes must be 4000 chars or fewer.", McpErrorCode.InvalidParams);
        }
    }

    public static void CheckTags(string? tags)
    {
        if (tags is not null && tags.Length > MaxTagsLength)
        {
            throw new McpProtocolException("Tags must be 500 chars or fewer.", McpErrorCode.InvalidParams);
        }
    }

    public static string? NormalizedChecklist(string? checklistJson)
    {
        var normalized = DailyChecklistJson.Normalize(checklistJson);
        if (normalized is not null && normalized.Length > MaxChecklistLength)
        {
            throw new McpProtocolException("Checklist must be 8000 chars or fewer.", McpErrorCode.InvalidParams);
        }

        return normalized;
    }

    public static string CleanChecklistText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 500)
        {
            throw new McpProtocolException("Checklist text must be 1 to 500 chars.", McpErrorCode.InvalidParams);
        }

        return text.Trim();
    }

    public static void CheckCustomLabel(string? customLabel)
    {
        if (customLabel is not null && customLabel.Length > MaxCustomLabelLength)
        {
            throw new McpProtocolException("Custom label must be 500 chars or fewer.", McpErrorCode.InvalidParams);
        }
    }

    public static void CheckDisplayName(string? displayName)
    {
        if (displayName is not null && displayName.Length > MaxDisplayNameLength)
        {
            throw new McpProtocolException("Display name must be 100 chars or fewer.", McpErrorCode.InvalidParams);
        }
    }

    public static void CheckDateFormat(string? dateFormat)
    {
        if (dateFormat is not null && (dateFormat.Length == 0 || dateFormat.Length > MaxDateFormatLength))
        {
            throw new McpProtocolException("Date format must be 1 to 20 chars.", McpErrorCode.InvalidParams);
        }
    }

    public static void CheckTimeZoneOverride(string? timeZoneOverrideId)
    {
        if (timeZoneOverrideId is not null && timeZoneOverrideId.Length > MaxTimeZoneLength)
        {
            throw new McpProtocolException("Timezone id must be 100 chars or fewer.", McpErrorCode.InvalidParams);
        }
    }

    public static int ResolveRepeatInterval(int? repeatInterval, int current)
    {
        if (repeatInterval is not { } interval)
        {
            return current;
        }

        if (interval is < 1 or > 999)
        {
            throw new McpProtocolException("Repeat interval must be 1 to 999.", McpErrorCode.InvalidParams);
        }

        return interval;
    }

    public static int ResolveDailyCounter(int? counter, int current)
    {
        if (counter is not { } value)
        {
            return current;
        }

        if (value is < 0 or > 9999)
        {
            throw new McpProtocolException("Counter must be 0 to 9999.", McpErrorCode.InvalidParams);
        }

        return value;
    }

    public static int ResolveHabitCounter(int? counter, int current, string name)
    {
        if (counter is not { } value)
        {
            return current;
        }

        if (value is < 0 or > 9999)
        {
            throw new McpProtocolException($"{name} must be 0 to 9999.", McpErrorCode.InvalidParams);
        }

        return value;
    }

    public static int ResolveWeekdays(int? weekdays, int current)
    {
        if (weekdays is not { } mask)
        {
            return current;
        }

        if (mask is < 0 or > 127)
        {
            throw new McpProtocolException("Weekdays must be 0 to 127.", McpErrorCode.InvalidParams);
        }

        return mask;
    }

    public static void CheckPomodoro(string name, int? value, int min, int max)
    {
        if (value is { } v && (v < min || v > max))
        {
            throw new McpProtocolException($"{name} must be {min} to {max}.", McpErrorCode.InvalidParams);
        }
    }

    public static TimeSpan ParseTimeOfDay(string raw, string paramName)
    {
        if (!TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed)
            || parsed < TimeSpan.Zero
            || parsed >= TimeSpan.FromDays(1))
        {
            throw new McpProtocolException($"{paramName} must use HH:mm within one day.", McpErrorCode.InvalidParams);
        }

        return parsed;
    }

    public static DateOnly ParseDate(string raw, string paramName = "Date")
    {
        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new McpProtocolException($"{paramName} must use yyyy-MM-dd.", McpErrorCode.InvalidParams);
        }

        return parsed;
    }

    public static DateOnly? ParseNullableDate(string? raw, string paramName = "Date")
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return ParseDate(raw, paramName);
    }

    public static void CheckImportSize(string json)
    {
        if (json.Length > MaxImportJsonLength)
        {
            throw new McpProtocolException("Import JSON is too large. Keep it under 10 MB.", McpErrorCode.InvalidParams);
        }
    }
}
