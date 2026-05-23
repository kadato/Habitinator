using App.Shared.RCL.Models;

using Microsoft.EntityFrameworkCore;

namespace App.MAUI.Data;

[Index(nameof(UserKey), nameof(Section))]
public sealed class LocalBoardItemRow
{
    public Guid Id { get; set; }

    /// <summary>Normalized account key (email) bound to this row.</summary>
    public string UserKey { get; set; } = "";

    public BoardSection Section { get; set; }

    public string Title { get; set; } = "";

    public bool IsCompleted { get; set; }

    public int Counter { get; set; }

    public string? Notes { get; set; }

    public string? Tags { get; set; }

    public bool TrackPlus { get; set; } = true;

    public bool TrackMinus { get; set; } = true;

    public int NegativeCounter { get; set; }

    public HabitResetPeriod ResetPeriod { get; set; } = HabitResetPeriod.Daily;

    public DateOnly? DailyStartDate { get; set; }

    public DailyRepeatType DailyRepeat { get; set; } = DailyRepeatType.Daily;

    public int DailyRepeatInterval { get; set; } = 1;

    public string? ChecklistJson { get; set; }

    public DateOnly? DailyLastCompletedOn { get; set; }

    public DateOnly? TodoDueDate { get; set; }

    /// <summary>True until the server acknowledges a create for this client-generated id.</summary>
    public bool AwaitingServerCreate { get; set; }

    /// <summary>Last known server <c>UpdatedAtUtc</c> for If-Match; null for purely local rows.</summary>
    public DateTimeOffset? ServerUpdatedAtUtc { get; set; }

    /// <summary>Server creation time (display/audit only; not used for list ordering).</summary>
    public DateTimeOffset? CreatedAtUtc { get; set; }

    public double? SortOrder { get; set; }

    public BoardItem ToModel() => new(
        Id,
        Title,
        IsCompleted,
        Counter,
        Notes,
        Tags,
        TrackPlus,
        TrackMinus,
        NegativeCounter,
        ResetPeriod,
        DailyStartDate,
        DailyRepeat,
        DailyRepeatInterval,
        ChecklistJson,
        DailyLastCompletedOn,
        TodoDueDate,
        ServerUpdatedAtUtc,
        CreatedAtUtc,
        SortOrder);

    public static LocalBoardItemRow FromModel(BoardSection section, string userKey, BoardItem item, bool awaitingCreate)
    {
        return new LocalBoardItemRow
        {
            Id = item.Id,
            UserKey = userKey,
            Section = section,
            Title = item.Title,
            IsCompleted = item.IsCompleted,
            Counter = item.Counter,
            Notes = item.Notes,
            Tags = item.Tags,
            TrackPlus = item.TrackPlus,
            TrackMinus = item.TrackMinus,
            NegativeCounter = item.NegativeCounter,
            ResetPeriod = item.ResetPeriod,
            DailyStartDate = item.DailyStartDate,
            DailyRepeat = item.DailyRepeat,
            DailyRepeatInterval = item.DailyRepeatInterval,
            ChecklistJson = item.ChecklistJson,
            DailyLastCompletedOn = item.DailyLastCompletedOn,
            TodoDueDate = item.TodoDueDate,
            AwaitingServerCreate = awaitingCreate,
            ServerUpdatedAtUtc = item.ServerUpdatedAtUtc,
            CreatedAtUtc = item.CreatedAtUtc,
            SortOrder = item.SortOrder
        };
    }
}
