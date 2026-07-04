namespace App.Shared.RCL;

public static class PageTitles
{
    public const string Brand = "Habitinator";

    public const string Landing = "Habitinator - habits, dailies, and to-dos";

    public static string Page(string pageName) => $"{pageName.Trim()} - {Brand}";
}
