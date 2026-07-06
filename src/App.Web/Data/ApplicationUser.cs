using App.Shared.RCL.Models;

using Microsoft.AspNetCore.Identity;

namespace App.Web.Data;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public NotificationSettings? NotificationSettings { get; set; }

    public UserPreferences? UserPreferences { get; set; }
}
