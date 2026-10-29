using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using App.Web.Data;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace App.Web.Auth;

public sealed class JwtTokenService
{
    private static readonly JwtSecurityTokenHandler Handler = new();

    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public string CreateToken(ApplicationUser user, TimeSpan? lifetimeOverride = null, Guid? tokenId = null, string? scope = null)
    {
        List<Claim> claims =
        [
            new(AppClaims.Subject, user.Id.ToString()),
            new(AppClaims.Email, user.Email ?? string.Empty),
            new(AppClaims.NameIdentifier, user.Id.ToString()),
            new(AppClaims.Name, user.UserName ?? string.Empty)
        ];

        if (tokenId.HasValue)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Jti, tokenId.Value.ToString("D")));
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            claims.Add(new Claim("scope", scope));
        }

        var lifetime = lifetimeOverride ?? TimeSpan.FromMinutes(_options.ExpirationMinutes);
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: _credentials);

        return Handler.WriteToken(token);
    }
}
