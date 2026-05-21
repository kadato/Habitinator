namespace App.Web.Services;

public sealed class DemoUserOptions
{
    public const string SectionName = "DemoUser";

    public string Email { get; set; } = "guest@habitinator.local";

    public string Password { get; set; } = "Guest123!";

    /// <summary>
    /// When true, the demo guest's board, activity log, and notification settings are cleared, the
    /// password is reset to <see cref="Password" />, and full demo data is inserted again. Use for
    /// local dev or a one-time refresh; can be set with environment variable
    /// <c>DemoUser__ForceReseed=true</c>.
    /// </summary>
    public bool ForceReseed { get; set; }

    /// <summary>
    /// When true, only the guest activity log is cleared and regenerated (board items are kept).
    /// Set with <c>DemoUser__ForceReseedActivity=true</c>.
    /// </summary>
    public bool ForceReseedActivity { get; set; }
}
