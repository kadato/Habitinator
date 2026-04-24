namespace App.Web.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "Habitinator";

    public string Audience { get; set; } = "HabitinatorClients";

    public string SigningKey { get; set; } = "replace-with-long-random-key-change-in-production";

    public int ExpirationMinutes { get; set; } = 60;
}
