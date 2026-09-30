using App.Shared.RCL.Models;

namespace App.Shared.RCL.Services.Board.Local;

/// <summary>Plain board row for local-first stores, with the same fields as <c>LocalBoardItemRow</c>.</summary>
public sealed class BoardLocalRow
{
    public Guid Id { get; set; }

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

    public bool IsArchived { get; set; }

    public bool AwaitingServerCreate { get; set; }

    public DateTimeOffset? ServerUpdatedAtUtc { get; set; }

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
        SortOrder,
        IsArchived);

    public static BoardLocalRow FromModel(BoardSection section, string userKey, BoardItem item, bool awaitingCreate)
    {
        return new BoardLocalRow
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
            SortOrder = item.SortOrder,
            IsArchived = item.IsArchived
        };
    }

    public void CopyFrom(BoardLocalRow source)
    {
        Title = source.Title;
        IsCompleted = source.IsCompleted;
        Counter = source.Counter;
        Notes = source.Notes;
        Tags = source.Tags;
        TrackPlus = source.TrackPlus;
        TrackMinus = source.TrackMinus;
        NegativeCounter = source.NegativeCounter;
        ResetPeriod = source.ResetPeriod;
        DailyStartDate = source.DailyStartDate;
        DailyRepeat = source.DailyRepeat;
        DailyRepeatInterval = source.DailyRepeatInterval;
        ChecklistJson = source.ChecklistJson;
        DailyLastCompletedOn = source.DailyLastCompletedOn;
        TodoDueDate = source.TodoDueDate;
        ServerUpdatedAtUtc = source.ServerUpdatedAtUtc;
        CreatedAtUtc = source.CreatedAtUtc;
        SortOrder = source.SortOrder;
        IsArchived = source.IsArchived;
    }
}
