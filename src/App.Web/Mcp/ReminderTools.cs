using System.ComponentModel;

using App.Shared.RCL.Models;
using App.Web.Services;

using ModelContextProtocol.Server;

namespace App.Web.Mcp;

[McpServerToolType]
public sealed class ReminderTools(BoardPersistenceService boards, IHttpContextAccessor http)
{
    public sealed record DueDigestEntry(Guid Id, string Title, DateOnly? DueDate, BoardSection Section);

    /// <summary>
    /// One digest of dated items: overdue, due today, and due tomorrow.
    /// </summary>
    public sealed record DueDigest(
        IReadOnlyList<DueDigestEntry> Overdue,
        IReadOnlyList<DueDigestEntry> DueToday,
        IReadOnlyList<DueDigestEntry> DueTomorrow,
        int Total);

    [McpServerTool(Name = "get_due_digest", Title = "Get due digest", ReadOnly = true, OpenWorld = false)]
    [Description("Returns one batched digest of dated todos: overdue, due today, and due tomorrow. Each todo appears at most once. Pass snoozed item ids to exclude them.")]
    public async Task<DueDigest> GetDueDigest(
        [Description("Item ids to exclude (snoozed by the caller), or null.")]
        IReadOnlyList<Guid>? snoozedIds = null,
        CancellationToken cancellationToken = default)
    {
        var userId = McpUser.RequireId(http);
        var today = await boards.TodayAsync(userId, cancellationToken);
        var tomorrow = today.AddDays(1);
        var snoozed = snoozedIds is null ? [] : new HashSet<Guid>(snoozedIds);

        var snapshot = await boards.GetSnapshotAsync(userId, null, cancellationToken);
        var overdue = new List<DueDigestEntry>();
        var dueToday = new List<DueDigestEntry>();
        var dueTomorrow = new List<DueDigestEntry>();
        foreach (var todo in snapshot.Todos)
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
}
