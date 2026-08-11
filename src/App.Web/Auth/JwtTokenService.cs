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

    public string CreateToken(ApplicationUser user)
    {
        List<Claim> claims =
        [
            new(AppClaims.Subject, user.Id.ToString()),
            new(AppClaims.Email, user.Email ?? string.Empty),
            new(AppClaims.NameIdentifier, user.Id.ToString()),
            new(AppClaims.Name, user.UserName ?? string.Empty)
        ];

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes),
            signingCredentials: _credentials);

        return Handler.WriteToken(token);
    }
}
