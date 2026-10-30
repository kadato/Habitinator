namespace App.Web.Auth;

/// <summary>Information about a registered OAuth 2.1 client.</summary>
public sealed record OAuthClient(
    string ClientId,
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    DateTimeOffset CreatedAtUtc);

/// <summary>Active authorization code issued during the OAuth 2.1 authorization code flow.</summary>
public sealed record OAuthAuthorizationCode(
    string Code,
    Guid UserId,
    string ClientId,
    string ClientName,
    string RedirectUri,
    string Scope,
    string CodeChallenge,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Refresh token for rotating access tokens without user re-authentication.</summary>
public sealed record OAuthRefreshToken(
    string Token,
    Guid TokenId,
    Guid UserId,
    string ClientId,
    string Scope,
    DateTimeOffset ExpiresAtUtc);

/// <summary>RFC 7591 dynamic client registration request.</summary>
public sealed record OAuthClientRegistrationRequest(
    string? ClientName,
    IReadOnlyList<string>? RedirectUris,
    IReadOnlyList<string>? GrantTypes,
    IReadOnlyList<string>? ResponseTypes,
    string? TokenEndpointAuthMethod);

/// <summary>RFC 7591 dynamic client registration response.</summary>
public sealed record OAuthClientRegistrationResponse(
    string ClientId,
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> GrantTypes,
    IReadOnlyList<string> ResponseTypes,
    string TokenEndpointAuthMethod);

/// <summary>OAuth 2.1 token response.</summary>
public sealed record OAuthTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string? RefreshToken,
    string Scope);
