namespace App.Web.Data;

/// <summary>Per-user, per-UTC-day usage of the shared operator AI key.</summary>
public sealed class AiUsageDailyEntity
{
    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public DateOnly Day { get; set; }

    public int Requests { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }
}
