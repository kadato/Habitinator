namespace App.Web.Services;

public sealed class DemoUserOptions
{
    public const string SectionName = "DemoUser";

    public string Email { get; set; } = "guest@habitinator.local";

    public string Password { get; set; } = "Guest123!";

    public string Timezone { get; set; } = "Europe/Budapest";
}
