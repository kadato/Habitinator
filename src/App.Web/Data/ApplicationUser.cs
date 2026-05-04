using Microsoft.AspNetCore.Identity;

namespace App.Web.Data;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? NotificationSettingsJson { get; set; }

    public string? UserPreferencesJson { get; set; }
}
