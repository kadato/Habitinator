using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.WebUtilities;

namespace App.Web.Auth;

public interface IOAuthService
{
    OAuthClient RegisterClient(OAuthClientRegistrationRequest request);
    OAuthClient? GetClient(string clientId);
    bool ValidateRedirectUri(OAuthClient? client, string redirectUri);
    string CreateAuthorizationCode(Guid userId, string clientId, string clientName, string redirectUri, string scope, string codeChallenge);
    OAuthAuthorizationCode? ValidateAndConsumeCode(string code, string clientId, string redirectUri, string codeVerifier);
    string CreateRefreshToken(Guid tokenId, Guid userId, string clientId, string scope, TimeSpan lifetime);
    (OAuthRefreshToken TokenInfo, string NewRefreshToken)? ValidateAndRotateRefreshToken(string refreshToken, string clientId, TimeSpan lifetime);
    void RevokeRefreshToken(string refreshToken);
    void RevokeByTokenId(Guid tokenId);
}

public sealed class OAuthService : IOAuthService
{
    private static readonly TimeSpan DefaultCodeLifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, OAuthClient> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, OAuthAuthorizationCode> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, OAuthRefreshToken> _refreshTokens = new(StringComparer.Ordinal);

    public OAuthClient RegisterClient(OAuthClientRegistrationRequest request)
    {
        var clientId = "client_" + Guid.NewGuid().ToString("N");
        var clientName = string.IsNullOrWhiteSpace(request.ClientName) ? "External AI Assistant" : request.ClientName.Trim();
        var redirectUris = request.RedirectUris?.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToList()
            ?? (IReadOnlyList<string>)[];

        var client = new OAuthClient(
            ClientId: clientId,
            ClientName: clientName,
            RedirectUris: redirectUris,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        _clients[clientId] = client;
        return client;
    }

    public OAuthClient? GetClient(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        if (_clients.TryGetValue(clientId, out var client))
        {
            return client;
        }

        // Allow known default desktop/CLI client identifiers without prior registration
        return new OAuthClient(
            ClientId: clientId,
            ClientName: ResolveWellKnownClientName(clientId),
            RedirectUris: [],
            CreatedAtUtc: DateTimeOffset.UtcNow);
    }

    public bool ValidateRedirectUri(OAuthClient? client, string redirectUri)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            return false;
        }

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // Loopback URIs are allowed for native clients (RFC 8252 section 7.3)
        if (uri.IsLoopback)
        {
            return true;
        }

        // Custom desktop URI schemes
        if (string.Equals(uri.Scheme, "cursor", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Scheme, "claude", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Scheme, "vscode", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // If client registered specific redirect URIs, check against them
        if (client is not null && client.RedirectUris.Count > 0)
        {
            return client.RedirectUris.Any(u => string.Equals(u, redirectUri, StringComparison.OrdinalIgnoreCase));
        }

        // For public HTTPS redirect URIs
        return uri.Scheme == Uri.UriSchemeHttps;
    }

    public string CreateAuthorizationCode(Guid userId, string clientId, string clientName, string redirectUri, string scope, string codeChallenge)
    {
        var code = "code_" + Guid.NewGuid().ToString("N") + RandomNumberGenerator.GetHexString(16);
        var authCode = new OAuthAuthorizationCode(
            Code: code,
            UserId: userId,
            ClientId: clientId,
            ClientName: clientName,
            RedirectUri: redirectUri,
            Scope: scope,
            CodeChallenge: codeChallenge,
            ExpiresAtUtc: DateTimeOffset.UtcNow.Add(DefaultCodeLifetime));

        _codes[code] = authCode;
        return code;
    }

    public OAuthAuthorizationCode? ValidateAndConsumeCode(string code, string clientId, string redirectUri, string codeVerifier)
    {
        if (string.IsNullOrWhiteSpace(code) || !_codes.TryRemove(code, out var authCode))
        {
            return null;
        }

        if (DateTimeOffset.UtcNow > authCode.ExpiresAtUtc)
        {
            return null;
        }

        if (!string.Equals(authCode.ClientId, clientId, StringComparison.Ordinal))
        {
            return null;
        }

        if (!string.Equals(authCode.RedirectUri, redirectUri, StringComparison.Ordinal))
        {
            return null;
        }

        if (!VerifyPkce(codeVerifier, authCode.CodeChallenge))
        {
            return null;
        }

        return authCode;
    }

    public string CreateRefreshToken(Guid tokenId, Guid userId, string clientId, string scope, TimeSpan lifetime)
    {
        var token = "rt_" + Guid.NewGuid().ToString("N") + RandomNumberGenerator.GetHexString(24);
        var refreshToken = new OAuthRefreshToken(
            Token: token,
            TokenId: tokenId,
            UserId: userId,
            ClientId: clientId,
            Scope: scope,
            ExpiresAtUtc: DateTimeOffset.UtcNow.Add(lifetime));

        _refreshTokens[token] = refreshToken;
        return token;
    }

    public (OAuthRefreshToken TokenInfo, string NewRefreshToken)? ValidateAndRotateRefreshToken(string refreshToken, string clientId, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || !_refreshTokens.TryRemove(refreshToken, out var existing))
        {
            return null;
        }

        if (DateTimeOffset.UtcNow > existing.ExpiresAtUtc)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(clientId) && !string.Equals(existing.ClientId, clientId, StringComparison.Ordinal))
        {
            return null;
        }

        var newRefreshToken = CreateRefreshToken(existing.TokenId, existing.UserId, existing.ClientId, existing.Scope, lifetime);
        return (existing, newRefreshToken);
    }

    public void RevokeRefreshToken(string refreshToken)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            _refreshTokens.TryRemove(refreshToken, out _);
        }
    }

    public void RevokeByTokenId(Guid tokenId)
    {
        var matches = _refreshTokens.Where(kvp => kvp.Value.TokenId == tokenId).Select(kvp => kvp.Key).ToList();
        foreach (var key in matches)
        {
            _refreshTokens.TryRemove(key, out _);
        }
    }

    public static bool VerifyPkce(string codeVerifier, string codeChallenge)
    {
        if (string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(codeChallenge))
        {
            return false;
        }

        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var computed = WebEncoders.Base64UrlEncode(hash);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(codeChallenge));
    }

    private static string ResolveWellKnownClientName(string clientId)
    {
        return clientId.ToLowerInvariant() switch
        {
            "claude" or "claude-desktop" => "Claude Desktop",
            "cursor" => "Cursor",
            "vscode" => "VS Code",
            "opencode" => "OpenCode",
            "hermes" => "Hermes",
            _ => clientId
        };
    }
}
