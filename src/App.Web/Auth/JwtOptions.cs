using System.ComponentModel.DataAnnotations;

namespace App.Web.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "Habitinator";

    [Required]
    public string Audience { get; set; } = "HabitinatorClients";

    [Required]
    [MinLength(32, ErrorMessage = "JWT Signing Key must be at least 32 characters (256 bits).")]
    public string SigningKey { get; set; } = "replace-with-long-random-key-change-in-production";

    [Range(1, 10080)]
    public int ExpirationMinutes { get; set; } = 60;
}
