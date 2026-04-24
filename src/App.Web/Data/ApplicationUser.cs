using Microsoft.AspNetCore.Identity;

namespace App.Web.Data;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string Timezone { get; set; } = "Europe/Budapest";
}
