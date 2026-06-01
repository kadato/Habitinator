namespace App.Web;

public static class SeoDefaults
{
    public const string SiteName = "Habitinator";

    public const string DefaultTitle = "Habitinator — habits, dailies, and to-dos";

    public const string DefaultDescription =
        "Cross-platform productivity app for habits, scheduled dailies, and to-dos with a focus timer, activity history, and statistics. Web and mobile.";

    public const string NoIndexRobots = "noindex, nofollow";

    public const string IndexRobots = "index, follow";

    public static string CanonicalPathFor(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == "/") return "/";
        return relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
    }
}
