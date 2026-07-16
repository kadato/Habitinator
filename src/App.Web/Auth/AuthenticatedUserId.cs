using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace App.Web.Auth;

internal static class AuthenticatedUserId
{
    public static Guid? TryGet(ClaimsPrincipal? user)
    {
        var s = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(s, out var id))
        {
            return id;
        }

        s = user?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(s, out id) ? id : null;
    }
}
