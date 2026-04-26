using System.Security.Claims;

using App.Web.Data;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace App.Web.Services;

public sealed class DemoUserResolver
{
    private readonly DemoUserOptions _options;
    private readonly UserManager<ApplicationUser> _userManager;

    public DemoUserResolver(UserManager<ApplicationUser> userManager, IOptions<DemoUserOptions> options)
    {
        _userManager = userManager;
        _options = options.Value;
    }

    public async Task<Guid> ResolveUserIdAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var claimValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(claimValue, out var parsedUserId)) return parsedUserId;

        var guestUser = await _userManager.FindByEmailAsync(_options.Email);
        if (guestUser is null)
            throw new InvalidOperationException("Demo guest user does not exist. Seed data has not run.");

        return guestUser.Id;
    }
}
