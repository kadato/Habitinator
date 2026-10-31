using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Web.Data;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;

namespace App.Web.Auth;

public static class OAuthApiRoutes
{
    private const string InvalidRequest = "invalid_request";
    private const string InvalidGrant = "invalid_grant";
    private const string RefreshTokenParam = "refresh_token";
    private const string ClientIdParam = "client_id";
    private const string RedirectUriParam = "redirect_uri";

    private static readonly string[] ResponseTypesSupported = ["code"];
    private static readonly string[] GrantTypesSupported = ["authorization_code", RefreshTokenParam];
    private static readonly string[] CodeChallengeMethodsSupported = ["S256"];
    private static readonly string[] TokenEndpointAuthMethodsSupported = ["none"];
    private static readonly string[] ScopesSupported = [McpTokenScopes.Full, McpTokenScopes.TrackingOnly, McpTokenScopes.ReadOnly];

    public static void MapOAuthRoutes(this IEndpointRouteBuilder endpoints)
    {
        // RFC 9728 Protected Resource Metadata
        endpoints.MapGet("/.well-known/oauth-protected-resource", GetProtectedResourceMetadata)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("api");

        endpoints.MapGet("/.well-known/oauth-protected-resource/mcp", GetProtectedResourceMetadata)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("api");

        // RFC 8414 Authorization Server Metadata
        endpoints.MapGet("/.well-known/oauth-authorization-server", GetAuthorizationServerMetadata)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("api");

        endpoints.MapGet("/.well-known/openid-configuration", GetAuthorizationServerMetadata)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("api");

        // RFC 7591 Dynamic Client Registration
        endpoints.MapPost("/oauth/register", RegisterClientAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("auth");

        // OAuth 2.1 Authorization Endpoint (GET displays consent UI, POST processes consent)
        endpoints.MapGet("/oauth/authorize", AuthorizeGetAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("auth");

        endpoints.MapPost("/oauth/authorize", AuthorizePostAsync)
            .RequireAuthorization("BoardOrJwt")
            .DisableAntiforgery()
            .RequireRateLimiting("auth");

        // OAuth 2.1 Token Endpoint
        endpoints.MapPost("/oauth/token", TokenAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("auth");

        // RFC 7009 Token Revocation Endpoint
        endpoints.MapPost("/oauth/revoke", RevokeAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("auth");
    }

    private static IResult GetProtectedResourceMetadata(HttpContext context)
    {
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}".TrimEnd('/');
        return Results.Ok(new Dictionary<string, object?>
        {
            ["resource"] = $"{baseUrl}/mcp",
            ["authorization_servers"] = new[] { baseUrl },
            ["scopes_supported"] = ScopesSupported
        });
    }

    private static IResult GetAuthorizationServerMetadata(HttpContext context)
    {
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}".TrimEnd('/');
        return Results.Ok(new Dictionary<string, object?>
        {
            ["issuer"] = baseUrl,
            ["authorization_endpoint"] = $"{baseUrl}/oauth/authorize",
            ["token_endpoint"] = $"{baseUrl}/oauth/token",
            ["registration_endpoint"] = $"{baseUrl}/oauth/register",
            ["revocation_endpoint"] = $"{baseUrl}/oauth/revoke",
            ["response_types_supported"] = ResponseTypesSupported,
            ["grant_types_supported"] = GrantTypesSupported,
            ["code_challenge_methods_supported"] = CodeChallengeMethodsSupported,
            ["token_endpoint_auth_methods_supported"] = TokenEndpointAuthMethodsSupported,
            ["scopes_supported"] = ScopesSupported
        });
    }

    private static IResult RegisterClientAsync(
        OAuthClientRegistrationRequest request,
        IOAuthService oauthService)
    {
        var client = oauthService.RegisterClient(request);
        var response = new OAuthClientRegistrationResponse(
            ClientId: client.ClientId,
            ClientName: client.ClientName,
            RedirectUris: client.RedirectUris,
            GrantTypes: GrantTypesSupported,
            ResponseTypes: ResponseTypesSupported,
            TokenEndpointAuthMethod: "none");

        return Results.Json(response, statusCode: (int)HttpStatusCode.Created);
    }

    private static async Task<IResult> AuthorizeGetAsync(
        HttpContext context,
        IOAuthService oauthService)
    {
        var responseType = context.Request.Query["response_type"].ToString();
        var clientId = context.Request.Query[ClientIdParam].ToString();
        var redirectUri = context.Request.Query[RedirectUriParam].ToString();
        var scope = context.Request.Query["scope"].ToString();
        var state = context.Request.Query["state"].ToString();
        var codeChallenge = context.Request.Query["code_challenge"].ToString();
        var codeChallengeMethod = context.Request.Query["code_challenge_method"].ToString();

        var client = oauthService.GetClient(clientId);
        var validationError = ValidateAuthorizeRequest(client, responseType, redirectUri, codeChallenge, codeChallengeMethod, oauthService);
        if (validationError is not null)
        {
            return validationError;
        }

        var user = await EnsureAuthenticatedUserAsync(context);
        if (user?.Identity?.IsAuthenticated != true)
        {
            var returnUrl = context.Request.Path + context.Request.QueryString;
            return Results.LocalRedirect($"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var normalizedScope = McpTokenScopes.Normalize(string.IsNullOrWhiteSpace(scope) ? McpTokenScopes.Full : scope);
        var clientName = client?.ClientName ?? (string.IsNullOrWhiteSpace(clientId) ? "External AI Assistant" : clientId);
        var userName = user.Identity?.Name ?? "User";

        var html = RenderConsentHtml(
            clientName: clientName,
            clientId: clientId,
            redirectUri: redirectUri,
            scope: normalizedScope,
            state: state,
            codeChallenge: codeChallenge,
            userName: userName);

        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static IResult? ValidateAuthorizeRequest(
        OAuthClient? client,
        string responseType,
        string redirectUri,
        string codeChallenge,
        string codeChallengeMethod,
        IOAuthService oauthService)
    {
        if (string.IsNullOrWhiteSpace(redirectUri) || !oauthService.ValidateRedirectUri(client, redirectUri))
        {
            return Results.BadRequest(new { error = InvalidRequest, error_description = "Invalid or missing redirect_uri." });
        }

        if (responseType != "code")
        {
            return Results.BadRequest(new { error = "unsupported_response_type", error_description = "Only 'code' response type is supported." });
        }

        if (string.IsNullOrWhiteSpace(codeChallenge) || !string.Equals(codeChallengeMethod, "S256", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = InvalidRequest, error_description = "code_challenge and code_challenge_method=S256 are required by OAuth 2.1." });
        }

        return null;
    }

    private static async Task<ClaimsPrincipal?> EnsureAuthenticatedUserAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            return context.User;
        }

        var cookieAuth = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (cookieAuth.Succeeded && cookieAuth.Principal is not null)
        {
            context.User = cookieAuth.Principal;
            return cookieAuth.Principal;
        }

        var jwtAuth = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (jwtAuth.Succeeded && jwtAuth.Principal is not null)
        {
            context.User = jwtAuth.Principal;
            return jwtAuth.Principal;
        }

        return null;
    }

    private static IResult AuthorizePostAsync(
        HttpContext context,
        IOAuthService oauthService)
    {
        var form = context.Request.Form;
        var action = form["action"].ToString();
        var clientId = form[ClientIdParam].ToString();
        var redirectUri = form[RedirectUriParam].ToString();
        var scope = form["scope"].ToString();
        var state = form["state"].ToString();
        var codeChallenge = form["code_challenge"].ToString();

        var client = oauthService.GetClient(clientId);
        if (string.IsNullOrWhiteSpace(redirectUri) || !oauthService.ValidateRedirectUri(client, redirectUri))
        {
            return Results.BadRequest(new { error = InvalidRequest, error_description = "Invalid redirect_uri." });
        }

        if (action == "deny")
        {
            var denyUrl = $"{redirectUri}?error=access_denied&error_description={Uri.EscapeDataString("The user denied the authorization request.")}";
            if (!string.IsNullOrWhiteSpace(state))
            {
                denyUrl += $"&state={Uri.EscapeDataString(state)}";
            }
            return Results.Redirect(denyUrl);
        }

        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst(AppClaims.Subject)?.Value;

        if (string.IsNullOrWhiteSpace(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var clientName = client?.ClientName ?? (string.IsNullOrWhiteSpace(clientId) ? "External AI Assistant" : clientId);
        var normalizedScope = McpTokenScopes.Normalize(string.IsNullOrWhiteSpace(scope) ? McpTokenScopes.Full : scope);

        var code = oauthService.CreateAuthorizationCode(
            userId: userId,
            clientId: clientId,
            clientName: clientName,
            redirectUri: redirectUri,
            scope: normalizedScope,
            codeChallenge: codeChallenge);

        var targetUrl = $"{redirectUri}?code={Uri.EscapeDataString(code)}";
        if (!string.IsNullOrWhiteSpace(state))
        {
            targetUrl += $"&state={Uri.EscapeDataString(state)}";
        }

        return Results.Redirect(targetUrl);
    }

    private sealed record TokenRequest(
        string? GrantType,
        string? Code,
        string? RedirectUri,
        string? ClientId,
        string? CodeVerifier,
        string? RefreshToken);

    private static async Task<TokenRequest> ReadTokenRequestAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            return new TokenRequest(
                GrantType: form["grant_type"].ToString(),
                Code: form["code"].ToString(),
                RedirectUri: form[RedirectUriParam].ToString(),
                ClientId: form[ClientIdParam].ToString(),
                CodeVerifier: form["code_verifier"].ToString(),
                RefreshToken: form[RefreshTokenParam].ToString());
        }

        if (context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
            var root = doc.RootElement;
            return new TokenRequest(
                GrantType: root.TryGetProperty("grant_type", out var gt) ? gt.GetString() : null,
                Code: root.TryGetProperty("code", out var cd) ? cd.GetString() : null,
                RedirectUri: root.TryGetProperty(RedirectUriParam, out var ru) ? ru.GetString() : null,
                ClientId: root.TryGetProperty(ClientIdParam, out var ci) ? ci.GetString() : null,
                CodeVerifier: root.TryGetProperty("code_verifier", out var cv) ? cv.GetString() : null,
                RefreshToken: root.TryGetProperty(RefreshTokenParam, out var rt) ? rt.GetString() : null);
        }

        return new TokenRequest(null, null, null, null, null, null);
    }

    private static async Task<IResult> TokenAsync(
        HttpContext context,
        IOAuthService oauthService,
        UserManager<ApplicationUser> userManager,
        JwtTokenService jwtTokenService,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var request = await ReadTokenRequestAsync(context, cancellationToken);

        if (string.Equals(request.GrantType, "authorization_code", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleAuthorizationCodeGrantAsync(request, oauthService, userManager, jwtTokenService, dbContext, cancellationToken);
        }

        if (string.Equals(request.GrantType, RefreshTokenParam, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleRefreshTokenGrantAsync(request, oauthService, userManager, jwtTokenService, dbContext, cancellationToken);
        }

        return Results.BadRequest(new { error = "unsupported_grant_type", error_description = "Only 'authorization_code' and 'refresh_token' are supported." });
    }

    private static async Task<IResult> HandleAuthorizationCodeGrantAsync(
        TokenRequest request,
        IOAuthService oauthService,
        UserManager<ApplicationUser> userManager,
        JwtTokenService jwtTokenService,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.CodeVerifier) ||
            string.IsNullOrWhiteSpace(request.ClientId) ||
            string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return Results.BadRequest(new { error = InvalidRequest, error_description = "code, code_verifier, client_id, and redirect_uri are required." });
        }

        var authCode = oauthService.ValidateAndConsumeCode(request.Code, request.ClientId, request.RedirectUri, request.CodeVerifier);
        if (authCode is null)
        {
            return Results.BadRequest(new { error = InvalidGrant, error_description = "The authorization code is invalid, expired, or the PKCE verifier did not match." });
        }

        var user = await userManager.FindByIdAsync(authCode.UserId.ToString());
        if (user is null)
        {
            return Results.BadRequest(new { error = InvalidGrant, error_description = "User not found." });
        }

        var now = DateTimeOffset.UtcNow;
        var tokenLifetime = TimeSpan.FromDays(30);
        var mcpTokenEntity = new McpTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Label = $"{authCode.ClientName} (OAuth)",
            Scope = authCode.Scope,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(tokenLifetime)
        };

        dbContext.McpTokens.Add(mcpTokenEntity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var jwt = jwtTokenService.CreateToken(user, tokenLifetime, mcpTokenEntity.Id, authCode.Scope);
        var newRefreshToken = oauthService.CreateRefreshToken(mcpTokenEntity.Id, user.Id, authCode.ClientId, authCode.Scope, tokenLifetime);

        var response = new OAuthTokenResponse(
            AccessToken: jwt,
            TokenType: "Bearer",
            ExpiresIn: (int)tokenLifetime.TotalSeconds,
            RefreshToken: newRefreshToken,
            Scope: authCode.Scope);

        return Results.Ok(response);
    }

    private static async Task<IResult> HandleRefreshTokenGrantAsync(
        TokenRequest request,
        IOAuthService oauthService,
        UserManager<ApplicationUser> userManager,
        JwtTokenService jwtTokenService,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.BadRequest(new { error = InvalidRequest, error_description = "refresh_token is required." });
        }

        var tokenLifetime = TimeSpan.FromDays(30);
        var rotated = oauthService.ValidateAndRotateRefreshToken(request.RefreshToken, request.ClientId ?? string.Empty, tokenLifetime);
        if (rotated is null)
        {
            return Results.BadRequest(new { error = InvalidGrant, error_description = "Refresh token is invalid or expired." });
        }

        var (existing, nextRefreshToken) = rotated.Value;
        var tokenRow = await dbContext.McpTokens.FindAsync([existing.TokenId], cancellationToken);
        if (tokenRow is null || tokenRow.RevokedAtUtc is not null)
        {
            oauthService.RevokeRefreshToken(nextRefreshToken);
            return Results.BadRequest(new { error = InvalidGrant, error_description = "Token has been revoked." });
        }

        var user = await userManager.FindByIdAsync(existing.UserId.ToString());
        if (user is null)
        {
            return Results.BadRequest(new { error = InvalidGrant, error_description = "User not found." });
        }

        var jwt = jwtTokenService.CreateToken(user, tokenLifetime, tokenRow.Id, existing.Scope);

        var response = new OAuthTokenResponse(
            AccessToken: jwt,
            TokenType: "Bearer",
            ExpiresIn: (int)tokenLifetime.TotalSeconds,
            RefreshToken: nextRefreshToken,
            Scope: existing.Scope);

        return Results.Ok(response);
    }

    private static async Task<IResult> RevokeAsync(
        HttpContext context,
        IOAuthService oauthService,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        string? token = null;
        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            token = form["token"].ToString();
        }
        else if (context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("token", out var t))
            {
                token = t.GetString();
            }
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            oauthService.RevokeRefreshToken(token);

            // If it's a GUID matching an McpTokenEntity ID
            if (Guid.TryParse(token, out var tokenId))
            {
                var row = await dbContext.McpTokens.FindAsync([tokenId], cancellationToken);
                if (row is not null && row.RevokedAtUtc is null)
                {
                    row.RevokedAtUtc = DateTimeOffset.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                oauthService.RevokeByTokenId(tokenId);
            }
        }

        return Results.Ok();
    }

    private static string RenderConsentHtml(
        string clientName,
        string clientId,
        string redirectUri,
        string scope,
        string? state,
        string codeChallenge,
        string userName)
    {
        var encoder = HtmlEncoder.Default;
        var safeClient = encoder.Encode(clientName);
        var safeUser = encoder.Encode(userName);
        var safeScope = encoder.Encode(scope);
        var safeRedirect = encoder.Encode(redirectUri);
        var safeClientId = encoder.Encode(clientId);
        var safeState = encoder.Encode(state ?? string.Empty);
        var safeChallenge = encoder.Encode(codeChallenge);

        var scopeDesc = scope switch
        {
            McpTokenScopes.TrackingOnly => "Complete habits and dailies, track timer sessions, and read board summaries.",
            McpTokenScopes.ReadOnly => "Read habits, dailies, todos, streaks, and board summaries.",
            _ => "Full access to create, edit, reorder, track, and manage your Habitinator boards."
        };

        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>Authorize {{safeClient}} - Habitinator</title>
            <style>
                :root {
                    --bg: #0f172a;
                    --card-bg: #1e293b;
                    --text: #f8fafc;
                    --text-secondary: #94a3b8;
                    --primary: #3b82f6;
                    --primary-hover: #2563eb;
                    --border: #334155;
                    --btn-cancel: #475569;
                }
                @media (prefers-color-scheme: light) {
                    :root {
                        --bg: #f8fafc;
                        --card-bg: #ffffff;
                        --text: #0f172a;
                        --text-secondary: #64748b;
                        --primary: #2563eb;
                        --primary-hover: #1d4ed8;
                        --border: #e2e8f0;
                        --btn-cancel: #e2e8f0;
                    }
                }
                body {
                    margin: 0;
                    padding: 0;
                    min-height: 100vh;
                    background-color: var(--bg);
                    color: var(--text);
                    font-family: system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                }
                .card {
                    background-color: var(--card-bg);
                    border: 1px solid var(--border);
                    border-radius: 1rem;
                    padding: 2rem;
                    max-width: 26rem;
                    width: 90%;
                    box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.2);
                }
                .header {
                    text-align: center;
                    margin-bottom: 1.5rem;
                }
                .badge {
                    display: inline-block;
                    padding: 0.25rem 0.75rem;
                    background: rgba(59, 130, 246, 0.15);
                    color: var(--primary);
                    border-radius: 9999px;
                    font-size: 0.8125rem;
                    font-weight: 600;
                    margin-bottom: 0.75rem;
                }
                h1 {
                    font-size: 1.35rem;
                    margin: 0 0 0.5rem 0;
                }
                p {
                    color: var(--text-secondary);
                    font-size: 0.9375rem;
                    line-height: 1.45;
                    margin: 0 0 1.25rem 0;
                }
                .permissions {
                    background: rgba(0, 0, 0, 0.05);
                    border: 1px solid var(--border);
                    border-radius: 0.5rem;
                    padding: 1rem;
                    margin-bottom: 1.5rem;
                    font-size: 0.875rem;
                }
                .permissions strong {
                    color: var(--text);
                    display: block;
                    margin-bottom: 0.25rem;
                }
                .actions {
                    display: flex;
                    gap: 0.75rem;
                }
                button {
                    flex: 1;
                    padding: 0.75rem 1rem;
                    font-size: 0.9375rem;
                    font-weight: 600;
                    border-radius: 0.5rem;
                    border: none;
                    cursor: pointer;
                    transition: background 0.15s ease;
                }
                .btn-primary {
                    background-color: var(--primary);
                    color: #ffffff;
                }
                .btn-primary:hover {
                    background-color: var(--primary-hover);
                }
                .btn-cancel {
                    background-color: transparent;
                    color: var(--text-secondary);
                    border: 1px solid var(--border);
                }
                .btn-cancel:hover {
                    background-color: rgba(0, 0, 0, 0.05);
                }
            </style>
        </head>
        <body>
            <div class="card">
                <div class="header">
                    <span class="badge">Habitinator AI Access</span>
                    <h1>Authorize {{safeClient}}</h1>
                    <p>Signed in as <strong>{{safeUser}}</strong></p>
                </div>
                <div class="permissions">
                    <strong>Requested Scope: {{safeScope}}</strong>
                    <div>{{scopeDesc}}</div>
                </div>
                <form method="post" action="/oauth/authorize">
                    <input type="hidden" name="client_id" value="{{safeClientId}}" />
                    <input type="hidden" name="redirect_uri" value="{{safeRedirect}}" />
                    <input type="hidden" name="scope" value="{{safeScope}}" />
                    <input type="hidden" name="state" value="{{safeState}}" />
                    <input type="hidden" name="code_challenge" value="{{safeChallenge}}" />
                    <div class="actions">
                        <button type="submit" name="action" value="deny" class="btn-cancel">Deny</button>
                        <button type="submit" name="action" value="approve" class="btn-primary">Allow Access</button>
                    </div>
                </form>
            </div>
        </body>
        </html>
        """;
    }
}
