using System.ComponentModel;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;
using App.Web.Services;

using Microsoft.EntityFrameworkCore;

using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class ReminderTools(
    BoardPersistenceService boards,
    IHttpContextAccessor http,
    ApplicationDbContext db,
    IClock clock,
    IUserTimeZoneService timeZone)
{
    public sealed record DueDigestEntry(Guid Id, string Title, DateOnly? DueDate, BoardSection Section);

    /// <summary>
    /// One digest of dated items: overdue, due today, and due tomorrow.
    /// Empty when reminders are disabled or quiet hours apply.
    /// </summary>
    public sealed record DueDigest(
        IReadOnlyList<DueDigestEntry> Overdue,
        IReadOnlyList<DueDigestEntry> DueToday,
        IReadOnlyList<DueDigestEntry> DueTomorrow,
        int Total);

    [McpServerTool(Name = "get_due_digest", Title = "Get due digest", ReadOnly = true, OpenWorld = false)]
    [Description("Returns one batched digest of dated todos: overdue, due today, and due tomorrow. Each todo appears at most once. Pass snoozed item ids to exclude them. Honors notification settings. The digest stays empty when daily reminders are off, when in-app messages are off, or during quiet hours.")]
    public async Task<DueDigest> GetDueDigest(
        [Description("Snoozed item ids to exclude, or null.")]
        IReadOnlyList<Guid>? snoozedIds = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var row = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.NotificationSettings, u.UserPreferences })
            .FirstOrDefaultAsync(cancellationToken);
        var settings = row?.NotificationSettings ?? NotificationSettings.CreateDefault();
        var prefs = row?.UserPreferences;

        timeZone.SetOverride(prefs?.TimeZoneOverrideId);
        if (!settings.InAppMessagesEnabled || !settings.DailyReminderEnabled)
        {
            return new DueDigest([], [], [], 0);
        }

        if (IsInQuietHours(settings, clock.UtcNow.UtcDateTime))
        {
            return new DueDigest([], [], [], 0);
        }

        var today = DailySchedule.LocalToday(clock, timeZone, prefs?.DayStartLocalTime);
        var tomorrow = today.AddDays(1);
        var snoozed = snoozedIds is null ? [] : new HashSet<Guid>(snoozedIds);

        var snapshot = await boards.GetSnapshotAsync(userId, null, cancellationToken);
        return BuildDigest(snapshot.Todos, today, tomorrow, snoozed);
    }

    internal static DueDigest BuildDigest(
        IReadOnlyList<BoardItem> todos,
        DateOnly today,
        DateOnly tomorrow,
        HashSet<Guid> snoozed)
    {
        var overdue = new List<DueDigestEntry>();
        var dueToday = new List<DueDigestEntry>();
        var dueTomorrow = new List<DueDigestEntry>();
        foreach (var todo in todos)
        {
            if (todo.IsCompleted || snoozed.Contains(todo.Id) || todo.TodoDueDate is not { } due)
            {
                continue;
            }

            var entry = new DueDigestEntry(todo.Id, todo.Title, due, BoardSection.Todo);
            if (due < today)
            {
                overdue.Add(entry);
            }
            else if (due == today)
            {
                dueToday.Add(entry);
            }
            else if (due == tomorrow)
            {
                dueTomorrow.Add(entry);
            }
        }

        return new DueDigest(overdue, dueToday, dueTomorrow, overdue.Count + dueToday.Count + dueTomorrow.Count);
    }

    private bool IsInQuietHours(NotificationSettings settings, DateTime utcNow)
    {
        if (!settings.QuietHoursEnabled
            || !settings.QuietHoursStartUtc.HasValue
            || !settings.QuietHoursEndUtc.HasValue)
        {
            return false;
        }

        var localTime = timeZone.ConvertToLocal(new DateTimeOffset(utcNow, TimeSpan.Zero));
        var localTimeOfDay = localTime.TimeOfDay;
        var quietStartLocal = timeZone.ConvertUtcTimeToLocal(settings.QuietHoursStartUtc.Value);
        var quietEndLocal = timeZone.ConvertUtcTimeToLocal(settings.QuietHoursEndUtc.Value);
        if (quietStartLocal == quietEndLocal)
        {
            return false;
        }

        if (quietStartLocal < quietEndLocal)
        {
            return localTimeOfDay >= quietStartLocal && localTimeOfDay < quietEndLocal;
        }

        return localTimeOfDay >= quietStartLocal || localTimeOfDay < quietEndLocal;
    }
}
