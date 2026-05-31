using App.Shared.RCL.Models;

namespace App.Web.Data;

public sealed class BoardItemEntity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = default!;

    public BoardSection Section { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string? Tags { get; set; }

    public bool TrackPlus { get; set; } = true;

    public bool TrackMinus { get; set; } = true;

    public int ResetPeriod { get; set; }

    public bool IsCompleted { get; set; }

    public int Counter { get; set; }

    public int NegativeCounter { get; set; }

    /// <summary>UTC date (time ignored) for daily scheduling.</summary>
    public DateTime? DailyStartDate { get; set; }

    public int DailyRepeatType { get; set; }

    public int DailyRepeatInterval { get; set; } = 1;

    public string? ChecklistJson { get; set; }

    public DateTime? DailyLastCompletedOn { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>User-defined sort position within a section. New items get max+1; drag-and-drop sets midpoint between neighbours.</summary>
    public double SortOrder { get; set; }

    /// <summary>When set, the row is a tombstone (soft-deleted). Live board queries exclude these.</summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <summary>True if the item is archived and hidden from the active board.</summary>
    public bool IsArchived { get; set; } = false;
}
