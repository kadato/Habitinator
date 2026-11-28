namespace App.Web.Data;

/// <summary>
/// Marks an account as a temporary copy of the seeded demo board. Each guest login gets its
/// own copy so edits cannot reach another visitor, and cleanup removes copies after they expire.
/// </summary>
public sealed class DemoGuestCloneEntity
{
    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
