namespace App.Shared.RCL.Services;

/// <summary>
/// Parses the inline quick-add syntax shared by the command palette, board columns, and the
/// upcoming quick add. Supported hints: <c>#tag</c>, <c>due:today</c>, <c>due:tomorrow</c>,
/// <c>due:yyyy-MM-dd</c>, <c>due:mon</c>, <c>@today</c>, <c>@tomorrow</c>, and
/// <c>!urgent</c>, <c>!p1</c>, or <c>!high</c>.
/// </summary>
public static class BoardQuickAddParser
{
    public static (string Title, string? Tags, DateOnly? DueDate) ParseInlineTitle(string input, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var tags = new List<string>();
        DateOnly? dueDate = null;
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var kept = new List<string>();
        var currentDay = today ?? DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var word in words)
        {
            if (word.StartsWith('#') && word.Length > 1)
            {
                tags.Add(word[1..].Trim());
            }
            else if (word.StartsWith("due:", StringComparison.OrdinalIgnoreCase) && word.Length > 4)
            {
                var value = word[4..].Trim();
                if (value.Equals("today", StringComparison.OrdinalIgnoreCase))
                {
                    dueDate = currentDay;
                }
                else if (value.Equals("tomorrow", StringComparison.OrdinalIgnoreCase))
                {
                    dueDate = currentDay.AddDays(1);
                }
                else if (DateOnly.TryParseExact(
                             value,
                             "yyyy-MM-dd",
                             System.Globalization.CultureInfo.InvariantCulture,
                             System.Globalization.DateTimeStyles.None,
                             out var parsed))
                {
                    dueDate = parsed;
                }
                else if (TryParseWeekday(value, currentDay, out var weekdayDate))
                {
                    dueDate = weekdayDate;
                }
                else
                {
                    kept.Add(word);
                }
            }
            else if (word.Equals("@today", StringComparison.OrdinalIgnoreCase))
            {
                dueDate = currentDay;
            }
            else if (word.Equals("@tomorrow", StringComparison.OrdinalIgnoreCase))
            {
                dueDate = currentDay.AddDays(1);
            }
            else if (word.Equals("!urgent", StringComparison.OrdinalIgnoreCase)
                     || word.Equals("!p1", StringComparison.OrdinalIgnoreCase)
                     || word.Equals("!high", StringComparison.OrdinalIgnoreCase))
            {
                if (!tags.Contains("urgent", StringComparer.OrdinalIgnoreCase))
                {
                    tags.Add("urgent");
                }
            }
            else
            {
                kept.Add(word);
            }
        }

        var title = kept.Count > 0 ? string.Join(' ', kept) : input.Trim();
        return (title, tags.Count > 0 ? string.Join(", ", tags) : null, dueDate);
    }

    private static bool TryParseWeekday(string value, DateOnly today, out DateOnly targetDate)
    {
        var days = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
        {
            ["mon"] = DayOfWeek.Monday,
            ["monday"] = DayOfWeek.Monday,
            ["tue"] = DayOfWeek.Tuesday,
            ["tuesday"] = DayOfWeek.Tuesday,
            ["wed"] = DayOfWeek.Wednesday,
            ["wednesday"] = DayOfWeek.Wednesday,
            ["thu"] = DayOfWeek.Thursday,
            ["thursday"] = DayOfWeek.Thursday,
            ["fri"] = DayOfWeek.Friday,
            ["friday"] = DayOfWeek.Friday,
            ["sat"] = DayOfWeek.Saturday,
            ["saturday"] = DayOfWeek.Saturday,
            ["sun"] = DayOfWeek.Sunday,
            ["sunday"] = DayOfWeek.Sunday
        };

        if (days.TryGetValue(value, out var targetDay))
        {
            var offset = ((int)targetDay - (int)today.DayOfWeek + 7) % 7;
            if (offset == 0)
            {
                offset = 7;
            }

            targetDate = today.AddDays(offset);
            return true;
        }

        targetDate = default;
        return false;
    }
}
