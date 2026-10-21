using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Web.Services;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

#pragma warning disable S1075 // MCP resource URIs are protocol identifiers, not file paths.
[McpServerResourceType]
public sealed class BoardResources(
    BoardPersistenceService boards,
    ActivityStatisticsService stats,
    IHttpContextAccessor http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [McpServerResource(UriTemplate = "board://snapshot", Name = "Board snapshot", Title = "Board snapshot", MimeType = "application/json")]
    [Description("Current board mirror, first 200 rows by sort order. Use get_snapshot for paging or search_items to find one item.")]
    public async Task<TextResourceContents> GetSnapshot(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);
        return new TextResourceContents
        {
            Uri = "board://snapshot",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(snapshot, Json),
        };
    }

    [McpServerResource(UriTemplate = "board://archived", Name = "Archived board", Title = "Archived board", MimeType = "application/json")]
    [Description("Archived items hidden from the active board, first 200 rows.")]
    public async Task<TextResourceContents> GetArchived(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var snapshot = await boards.GetArchivedSnapshotAsync(userId, 200, cancellationToken);
        return new TextResourceContents
        {
            Uri = "board://archived",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(snapshot, Json),
        };
    }

    [McpServerResource(UriTemplate = "board://item/{id}", Name = "Board item", Title = "Board item", MimeType = "application/json")]
    [Description("One board item by id.")]
    public async Task<TextResourceContents> GetItem(
        [Description("Board item id.")] string id,
        CancellationToken cancellationToken = default)
    {
        if (id.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return await GetItemMarkdown(id[..^3], cancellationToken);
        }

        var userId = McpUser.RequireId(http);
        if (!Guid.TryParse(id, out var itemId))
        {
            throw new McpProtocolException($"Invalid item id: {id}", McpErrorCode.InvalidParams);
        }

        var item = await boards.GetItemAsync(userId, itemId, cancellationToken)
            ?? throw new McpProtocolException($"Item not found: {id}", McpErrorCode.InvalidParams);

        return new TextResourceContents
        {
            Uri = $"board://item/{id}",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(item, Json),
        };
    }

    [McpServerResource(UriTemplate = "board://item/{id}.md", Name = "Board item markdown", Title = "Board item markdown", MimeType = "text/markdown")]
    [Description("One board item formatted as Markdown.")]
    public async Task<TextResourceContents> GetItemMarkdown(
        [Description("Board item id.")] string id,
        CancellationToken cancellationToken = default)
    {
        if (id.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            id = id[..^3];
        }

        var userId = McpUser.RequireId(http);
        if (!Guid.TryParse(id, out var itemId))
        {
            throw new McpProtocolException($"Invalid item id: {id}", McpErrorCode.InvalidParams);
        }

        var item = await boards.GetItemAsync(userId, itemId, cancellationToken)
            ?? throw new McpProtocolException($"Item not found: {id}", McpErrorCode.InvalidParams);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# {item.Title}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **ID:** `{item.Id:D}`");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Completed:** {item.IsCompleted}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Counter:** +{item.Counter} / -{item.NegativeCounter}");
        if (item.DailyStartDate.HasValue)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- **Start Date:** {item.DailyStartDate.Value:yyyy-MM-dd}");
        }

        if (item.TodoDueDate.HasValue)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- **Due Date:** {item.TodoDueDate.Value:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(item.Tags))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- **Tags:** {item.Tags}");
        }

        if (!string.IsNullOrWhiteSpace(item.Notes))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- **Notes:** {item.Notes}");
        }

        if (!string.IsNullOrWhiteSpace(item.ChecklistJson))
        {
            var checklist = DailyChecklistJson.Parse(item.ChecklistJson);
            if (checklist.Count > 0)
            {
                sb.AppendLine("- **Checklist:**");
                foreach (var c in checklist)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  - [{(c.IsDone ? "x" : " ")}] {c.Text}");
                }
            }
        }

        return new TextResourceContents
        {
            Uri = $"board://item/{id}.md",
            MimeType = "text/markdown",
            Text = sb.ToString(),
        };
    }

    [McpServerResource(UriTemplate = "activity://dashboard", Name = "Activity dashboard", Title = "Activity dashboard", MimeType = "application/json")]
    [Description("Activity dashboard heatmap data with default period.")]
    public async Task<TextResourceContents> GetDashboard(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var dashboard = await stats.GetDashboardAsync(userId, null, null, cancellationToken);
        return new TextResourceContents
        {
            Uri = "activity://dashboard",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(dashboard, Json),
        };
    }

    [McpServerResource(UriTemplate = "activity://day/{date}", Name = "Activity day", Title = "Activity day", MimeType = "application/json")]
    [Description("Activity detail for one date in yyyy-MM-dd form.")]
    public async Task<TextResourceContents> GetDay(
        [Description("Date as yyyy-MM-dd.")] string date,
        CancellationToken cancellationToken = default)
    {
        if (date.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return await GetDayMarkdown(date[..^3], cancellationToken);
        }

        var userId = McpUser.RequireId(http);
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new McpProtocolException($"Invalid date: {date}. Use yyyy-MM-dd.", McpErrorCode.InvalidParams);
        }

        var detail = await stats.GetActivityDayDetailAsync(userId, day, null, cancellationToken);
        return new TextResourceContents
        {
            Uri = $"activity://day/{date}",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(detail, Json),
        };
    }

    [McpServerResource(UriTemplate = "activity://day/{date}.md", Name = "Activity day markdown", Title = "Activity day markdown", MimeType = "text/markdown")]
    [Description("Activity detail for one date in Markdown format.")]
    public async Task<TextResourceContents> GetDayMarkdown(
        [Description("Date as yyyy-MM-dd.")] string date,
        CancellationToken cancellationToken = default)
    {
        if (date.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            date = date[..^3];
        }

        var userId = McpUser.RequireId(http);
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw new McpProtocolException($"Invalid date: {date}. Use yyyy-MM-dd.", McpErrorCode.InvalidParams);
        }

        var detail = await stats.GetActivityDayDetailAsync(userId, day, null, cancellationToken);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Activity Detail ({day:yyyy-MM-dd})");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Focus minutes:** {detail.FocusMinutesTotal} min");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Events logged:** {detail.Events.Count}");
        sb.AppendLine();
        if (detail.Events.Count > 0)
        {
            sb.AppendLine("### Events");
            foreach (var ev in detail.Events)
            {
                var title = ev.BoardItemTitle ?? ev.CustomLabel ?? "item";
                var time = ev.OccurredAtUtc.ToString("HH:mm", CultureInfo.InvariantCulture);
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{time}` **{ev.Label}**: {title}{(ev.DurationSeconds.HasValue ? $" ({ev.DurationSeconds.Value / 60}m)" : "")}");
            }
        }

        return new TextResourceContents
        {
            Uri = $"activity://day/{date}.md",
            MimeType = "text/markdown",
            Text = sb.ToString(),
        };
    }

    [McpServerResource(UriTemplate = "activity://dashboard.md", Name = "Activity dashboard markdown", Title = "Activity dashboard markdown", MimeType = "text/markdown")]
    [Description("Activity dashboard summary in compact Markdown format.")]
    public async Task<TextResourceContents> GetDashboardMarkdown(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var dashboard = await stats.GetDashboardAsync(userId, null, null, cancellationToken);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Activity Dashboard ({dashboard.RangeStart:yyyy-MM-dd} to {dashboard.RangeEnd:yyyy-MM-dd})");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Total events:** {dashboard.TotalEvents}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Total focus time:** {dashboard.TotalFocusMinutes} minutes");
        if (dashboard.BusiestDay.HasValue)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- **Busiest day:** {dashboard.BusiestDay.Value:yyyy-MM-dd} ({dashboard.MaxDayCount} events)");
        }
        sb.AppendLine();
        if (dashboard.WeekBars.Count > 0)
        {
            sb.AppendLine("### Weekly Summary");
            foreach (var w in dashboard.WeekBars.TakeLast(8))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- Week of {w.WeekStart:yyyy-MM-dd}: {w.EventCount} events, {w.FocusMinutes} min focus");
            }
        }

        return new TextResourceContents
        {
            Uri = "activity://dashboard.md",
            MimeType = "text/markdown",
            Text = sb.ToString(),
        };
    }

    [McpServerResource(UriTemplate = "board://summary", Name = "Board summary", Title = "Board summary", MimeType = "application/json")]
    [Description("Summary of active habits, completed and pending dailies, active and overdue todos, and streaks.")]
    public async Task<TextResourceContents> GetSummary(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);
        var today = await boards.TodayAsync(userId, cancellationToken);

        var summary = new
        {
            HabitsCount = snapshot.Habits.Count,
            TotalHabitTicks = snapshot.Habits.Sum(h => h.Counter),
            DailiesCount = snapshot.Dailies.Count,
            DailiesCompletedToday = snapshot.Dailies.Count(d => d.IsCompleted),
            DailiesPendingToday = snapshot.Dailies.Count(d => !d.IsCompleted),
            TodosActiveCount = snapshot.Todos.Count(t => !t.IsCompleted),
            TodosCompletedCount = snapshot.Todos.Count(t => t.IsCompleted),
            TodosOverdueCount = snapshot.Todos.Count(t => !t.IsCompleted && t.TodoDueDate.HasValue && t.TodoDueDate.Value < today),
            TopStreaks = snapshot.Dailies
                .Where(d => d.Counter > 0)
                .OrderByDescending(d => d.Counter)
                .Take(5)
                .Select(d => new { d.Title, Streak = d.Counter })
                .ToList()
        };

        return new TextResourceContents
        {
            Uri = "board://summary",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(summary, Json),
        };
    }

    [McpServerResource(UriTemplate = "board://summary.md", Name = "Board summary markdown", Title = "Board summary markdown", MimeType = "text/markdown")]
    [Description("Markdown overview of today's status, pending dailies, overdue todos, and top habits.")]
    public async Task<TextResourceContents> GetSummaryMarkdown(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var snapshot = await boards.GetSnapshotAsync(userId, 200, cancellationToken);
        var today = await boards.TodayAsync(userId, cancellationToken);

        var pendingDailies = snapshot.Dailies.Where(d => !d.IsCompleted).ToList();
        var doneDailies = snapshot.Dailies.Where(d => d.IsCompleted).ToList();
        var overdueTodos = snapshot.Todos.Where(t => !t.IsCompleted && t.TodoDueDate.HasValue && t.TodoDueDate.Value < today).ToList();
        var activeTodos = snapshot.Todos.Where(t => !t.IsCompleted && (!t.TodoDueDate.HasValue || t.TodoDueDate.Value >= today)).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Board Summary ({today:yyyy-MM-dd})");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Dailies:** {doneDailies.Count}/{snapshot.Dailies.Count} done");
        if (pendingDailies.Count > 0)
        {
            sb.AppendLine("### Pending Dailies");
            foreach (var d in pendingDailies.Take(10))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {d.Title} (streak: {d.Counter})");
            }
        }
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Todos:** {activeTodos.Count} active, {overdueTodos.Count} overdue");
        if (overdueTodos.Count > 0)
        {
            sb.AppendLine("### Overdue Todos");
            foreach (var t in overdueTodos.Take(10))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- [!] {t.Title} (due: {t.TodoDueDate!.Value:yyyy-MM-dd})");
            }
        }
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Habits:** {snapshot.Habits.Count} tracked");
        foreach (var h in snapshot.Habits.Take(5))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {h.Title}: +{h.Counter} / -{h.NegativeCounter}");
        }

        return new TextResourceContents
        {
            Uri = "board://summary.md",
            MimeType = "text/markdown",
            Text = sb.ToString(),
        };
    }

    [McpServerResource(UriTemplate = "board://tags", Name = "Board tags", Title = "Board tags list", MimeType = "application/json")]
    [Description("List of distinct tags currently in use across board items.")]
    public async Task<TextResourceContents> GetTags(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var tags = await boards.GetDistinctTagsAsync(userId, cancellationToken);
        return new TextResourceContents
        {
            Uri = "board://tags",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(tags, Json),
        };
    }

    [McpServerResource(UriTemplate = "board://streaks", Name = "Daily streaks", Title = "Daily streaks map", MimeType = "application/json")]
    [Description("Current streak count for each daily item.")]
    public async Task<TextResourceContents> GetStreaksResource(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var streaks = await boards.GetDailyStreakMapAsync(userId, cancellationToken);
        return new TextResourceContents
        {
            Uri = "board://streaks",
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(streaks, Json),
        };
    }

    [McpServerResource(UriTemplate = "board://archived.md", Name = "Archived board markdown", Title = "Archived board markdown", MimeType = "text/markdown")]
    [Description("Archived board items formatted as Markdown.")]
    public async Task<TextResourceContents> GetArchivedMarkdown(CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var snapshot = await boards.GetArchivedSnapshotAsync(userId, 200, cancellationToken);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Archived Items");
        sb.AppendLine();
        if (snapshot.Habits.Count > 0)
        {
            sb.AppendLine("### Habits");
            foreach (var h in snapshot.Habits)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{h.Id:D}` {h.Title} (+{h.Counter}/-{h.NegativeCounter})");
            }
            sb.AppendLine();
        }
        if (snapshot.Dailies.Count > 0)
        {
            sb.AppendLine("### Dailies");
            foreach (var d in snapshot.Dailies)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{d.Id:D}` {d.Title} (streak: {d.Counter})");
            }
            sb.AppendLine();
        }
        if (snapshot.Todos.Count > 0)
        {
            sb.AppendLine("### Todos");
            foreach (var t in snapshot.Todos)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{t.Id:D}` [{(t.IsCompleted ? "x" : " ")}] {t.Title}{(t.TodoDueDate.HasValue ? $" (due: {t.TodoDueDate.Value:yyyy-MM-dd})" : "")}");
            }
        }

        return new TextResourceContents
        {
            Uri = "board://archived.md",
            MimeType = "text/markdown",
            Text = sb.ToString(),
        };
    }
}
