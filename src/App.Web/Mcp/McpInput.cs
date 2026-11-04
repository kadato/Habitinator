using System.Globalization;

using App.Shared.RCL.Models;

using ModelContextProtocol;

namespace App.Web.Mcp;

internal static class McpInput
{
    public const int MaxTitleLength = 200;
    public const int MaxNotesLength = 4000;
    public const int MaxTagsLength = BoardTagUtil.MaxTagsLength;
    public const int MaxChecklistLength = 8000;
    public const int MaxCustomLabelLength = 500;
    public const int MaxDisplayNameLength = 100;
    public const int MaxDateFormatLength = 20;
    public const int MaxTimeZoneLength = 100;
    public const int MaxImportJsonLength = 10_000_000;

    private const int MaxReceivedPreview = 120;

    /// <summary>One-line truncated preview of a received value for error messages.</summary>
    public static string PreviewReceived(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return singleLine.Length <= MaxReceivedPreview
            ? $"'{singleLine}'"
            : $"'{singleLine[..MaxReceivedPreview]}' (truncated)";
    }

    public static string CleanTitle(string title, string field = "title")
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitleLength)
        {
            throw new McpFieldException(field,
                $"expected 1 to {MaxTitleLength} chars, got {PreviewReceived(title)}.");
        }

        return title.Trim();
    }

    public static void CheckNotes(string? notes, string field = "notes")
    {
        if (notes is not null && notes.Length > MaxNotesLength)
        {
            throw new McpFieldException(field,
                $"expected {MaxNotesLength} chars or fewer, got {notes.Length} chars.");
        }
    }

    /// <summary>
    /// Validates an already-normalized tags string.
    /// Empty string clears, null keeps.
    /// </summary>
    public static void CheckTags(string? tags, string field = "tags")
    {
        if (tags is not null && tags.Length > MaxTagsLength)
        {
            throw new McpFieldException(field,
                $"expected comma-separated string (max {MaxTagsLength} chars), got {tags.Length} chars.");
        }
    }

    /// <summary>
    /// Canonical tags form shared by MCP, REST, and UI: trimmed tags joined with ",".
    /// Returns null only for null input.
    /// </summary>
    public static string? CanonicalTags(string? tags)
    {
        return BoardTagUtil.Canonicalize(tags);
    }

    public static string? NormalizedChecklist(string? checklistJson, string field = "checklistJson")
    {
        string? normalized;
        try
        {
            normalized = DailyChecklistJson.Normalize(checklistJson);
        }
        catch (Exception ex) when (ex is not McpProtocolException)
        {
            throw new McpFieldException(field,
                $"expected JSON array of {{text, isDone}} (max {MaxChecklistLength} chars), got {PreviewReceived(checklistJson)}.");
        }

        if (normalized is not null && normalized.Length > MaxChecklistLength)
        {
            throw new McpFieldException(field,
                $"expected JSON array of {{text, isDone}} (max {MaxChecklistLength} chars), got {normalized.Length} chars.");
        }

        return normalized;
    }

    public static string CleanChecklistText(string text, string field = "text")
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 500)
        {
            throw new McpFieldException(field,
                $"expected 1 to 500 chars, got {PreviewReceived(text)}.");
        }

        return text.Trim();
    }

    public static void CheckCustomLabel(string? customLabel, string field = "customLabel")
    {
        if (customLabel is not null && customLabel.Length > MaxCustomLabelLength)
        {
            throw new McpFieldException(field,
                $"expected {MaxCustomLabelLength} chars or fewer, got {customLabel.Length} chars.");
        }
    }

    public static void CheckDisplayName(string? displayName, string field = "displayName")
    {
        if (displayName is not null && displayName.Length > MaxDisplayNameLength)
        {
            throw new McpFieldException(field,
                $"expected {MaxDisplayNameLength} chars or fewer, got {displayName.Length} chars.");
        }
    }

    public static void CheckDateFormat(string? dateFormat, string field = "dateFormat")
    {
        if (dateFormat is not null && (dateFormat.Length == 0 || dateFormat.Length > MaxDateFormatLength))
        {
            throw new McpFieldException(field,
                $"expected 1 to {MaxDateFormatLength} chars, got {PreviewReceived(dateFormat)}.");
        }
    }

    public static void CheckTimeZoneOverride(string? timeZoneOverrideId, string field = "timeZone")
    {
        if (timeZoneOverrideId is not null && timeZoneOverrideId.Length > MaxTimeZoneLength)
        {
            throw new McpFieldException(field,
                $"expected {MaxTimeZoneLength} chars or fewer, got {timeZoneOverrideId.Length} chars.");
        }
    }

    public static int ResolveRepeatInterval(int? repeatInterval, int current, string field = "repeatInterval")
    {
        if (repeatInterval is not { } interval)
        {
            return current;
        }

        if (interval is < 1 or > 999)
        {
            throw new McpFieldException(field,
                $"expected 1 to 999, got {interval}.");
        }

        return interval;
    }

    public static int ResolveDailyCounter(int? counter, int current, string field = "counter")
    {
        if (counter is not { } value)
        {
            return current;
        }

        if (value is < 0 or > 9999)
        {
            throw new McpFieldException(field,
                $"expected 0 to 9999, got {value}.");
        }

        return value;
    }

    public static int ResolveHabitCounter(int? counter, int current, string name, string? field = null)
    {
        if (counter is not { } value)
        {
            return current;
        }

        if (value is < 0 or > 9999)
        {
            throw new McpFieldException(field ?? ToFieldName(name),
                $"expected 0 to 9999, got {value}.");
        }

        return value;
    }

    public static int ResolveWeekdays(int? weekdays, int current, string field = "weekdays")
    {
        if (weekdays is not { } mask)
        {
            return current;
        }

        if (mask is < 0 or > 127)
        {
            throw new McpFieldException(field,
                $"expected bitmask 0 to 127, got {mask}.");
        }

        return mask;
    }

    public static int ResolveWeekdaysWithNames(
        int? weekdays,
        IReadOnlyList<string>? weekdayNames,
        int current,
        string field = "weekdayNames")
    {
        if (weekdayNames is { Count: > 0 })
        {
            var mask = DailyWeekdays.None;
            foreach (var name in weekdayNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var trimmed = name.Trim();
                if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dayInt) && dayInt >= 0 && dayInt <= 6)
                {
                    mask |= 1 << dayInt;
                }
                else if (Enum.TryParse<DayOfWeek>(trimmed, ignoreCase: true, out var dow))
                {
                    mask |= 1 << (int)dow;
                }
                else
                {
                    var matched = trimmed.ToUpperInvariant() switch
                    {
                        "SUN" => DayOfWeek.Sunday,
                        "MON" => DayOfWeek.Monday,
                        "TUE" => DayOfWeek.Tuesday,
                        "WED" => DayOfWeek.Wednesday,
                        "THU" => DayOfWeek.Thursday,
                        "FRI" => DayOfWeek.Friday,
                        "SAT" => DayOfWeek.Saturday,
                        _ => (DayOfWeek?)null,
                    };
                    if (matched.HasValue)
                    {
                        mask |= 1 << (int)matched.Value;
                    }
                    else
                    {
                        throw new McpFieldException(field,
                            $"unrecognized weekday name {PreviewReceived(trimmed)}. Use Mon, Tue, Wed, Thu, Fri, Sat, Sun.");
                    }
                }
            }

            return DailyWeekdays.Normalize(mask);
        }

        return ResolveWeekdays(weekdays, current, field);
    }

    public static void CheckPomodoro(string name, int? value, int min, int max)
    {
        if (value is { } v && (v < min || v > max))
        {
            throw new McpFieldException(ToFieldName(name),
                $"expected {min} to {max}, got {v}.");
        }
    }

    public static TimeSpan ParseTimeOfDay(string raw, string paramName)
    {
        if (!TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed)
            || parsed < TimeSpan.Zero
            || parsed >= TimeSpan.FromDays(1))
        {
            throw new McpFieldException(ToFieldName(paramName),
                $"expected HH:mm within one day, got {PreviewReceived(raw)}.");
        }

        return parsed;
    }

    public static DateOnly ParseDate(string raw, string paramName = "date", DateOnly? today = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new McpFieldException(ToFieldName(paramName),
                "expected yyyy-MM-dd, 'today', 'yesterday', or 'tomorrow', got empty.");
        }

        var trimmed = raw.Trim();
        var baseDate = today ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (string.Equals(trimmed, "today", StringComparison.OrdinalIgnoreCase))
        {
            return baseDate;
        }

        if (string.Equals(trimmed, "yesterday", StringComparison.OrdinalIgnoreCase))
        {
            return baseDate.AddDays(-1);
        }

        if (string.Equals(trimmed, "tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            return baseDate.AddDays(1);
        }

        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        if (DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        throw new McpFieldException(ToFieldName(paramName),
            $"expected yyyy-MM-dd, 'today', 'yesterday', or 'tomorrow', got {PreviewReceived(raw)}.");
    }

    public static DateOnly? ParseNullableDate(string? raw, string paramName = "date", DateOnly? today = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return ParseDate(raw, paramName, today);
    }

    public static void CheckImportSize(string json, string field = "json")
    {
        if (json.Length > MaxImportJsonLength)
        {
            throw new McpFieldException(field,
                $"expected under 10 MB, got {json.Length} chars.");
        }
    }

    public static void CheckIdempotencyKey(string? key, string field = "idempotencyKey")
    {
        if (key is not null && key.Length > Services.BoardIdempotencyService.MaxIdempotencyKeyLength)
        {
            throw new McpFieldException(field,
                $"expected at most {Services.BoardIdempotencyService.MaxIdempotencyKeyLength} chars, got {key.Length} chars.");
        }
    }

    private static string ToFieldName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "value";
        }

        var trimmed = name.Trim();
        return trimmed.Length switch
        {
            0 => "value",
            1 => trimmed.ToLowerInvariant(),
            _ => char.ToLowerInvariant(trimmed[0]) + trimmed[1..],
        };
    }
}
