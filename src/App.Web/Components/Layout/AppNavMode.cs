namespace App.Web.Components.Layout;

public enum AppNavMode
{
    /// <summary>Board plus login/register for guests (single strip).</summary>
    Full,
    /// <summary>Board only (paired with the app title on the left).</summary>
    BoardOnly,
    /// <summary>Login / register in the right account rail (guests only).</summary>
    AuthLinks,
}
