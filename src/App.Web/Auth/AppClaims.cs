using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace App.Web.Auth;

internal static class AppClaims
{
    public const string Subject = JwtRegisteredClaimNames.Sub;
    public const string NameIdentifier = ClaimTypes.NameIdentifier;
    public const string Email = ClaimTypes.Email;
    public const string Name = ClaimTypes.Name;
}
