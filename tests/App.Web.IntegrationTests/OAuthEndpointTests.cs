using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Web.Auth;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class OAuthEndpointTests(PostgresWebAppFactory factory)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private HttpClient CreateNoRedirectClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    [Fact]
    public async Task ProtectedResourceMetadata_ReturnsCorrectResourceAndScopes()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("resource").GetString().Should().EndWith("/mcp");
        doc.GetProperty("authorization_servers").GetArrayLength().Should().BeGreaterThan(0);
        var scopes = doc.GetProperty("scopes_supported").EnumerateArray().Select(s => s.GetString()).ToList();
        scopes.Should().Contain(McpTokenScopes.Full);
        scopes.Should().Contain(McpTokenScopes.TrackingOnly);
        scopes.Should().Contain(McpTokenScopes.ReadOnly);

        var mcpPathResponse = await client.GetAsync("/.well-known/oauth-protected-resource/mcp");
        mcpPathResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthorizationServerMetadata_ReturnsStandardEndpoints()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/.well-known/oauth-authorization-server");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("issuer").GetString().Should().NotBeNullOrWhiteSpace();
        doc.GetProperty("authorization_endpoint").GetString().Should().EndWith("/oauth/authorize");
        doc.GetProperty("token_endpoint").GetString().Should().EndWith("/oauth/token");
        doc.GetProperty("registration_endpoint").GetString().Should().EndWith("/oauth/register");

        var codeChallengeMethods = doc.GetProperty("code_challenge_methods_supported")
            .EnumerateArray().Select(s => s.GetString()).ToList();
        codeChallengeMethods.Should().Contain("S256");

        var oidcResponse = await client.GetAsync("/.well-known/openid-configuration");
        oidcResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task McpWithoutToken_Returns401WithWwwAuthenticateMetadataHeader()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
                @params = new { }
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        response.Headers.WwwAuthenticate.Should().NotBeEmpty();
        var authHeader = response.Headers.WwwAuthenticate.ToString();
        authHeader.Should().Contain("Bearer");
        authHeader.Should().Contain("resource_metadata=");
        authHeader.Should().Contain("/.well-known/oauth-protected-resource");
    }

    [Fact]
    public async Task DynamicClientRegistration_CreatesClient()
    {
        var client = factory.CreateClient();
        var regRequest = new OAuthClientRegistrationRequest(
            ClientName: "Test Desktop AI",
            RedirectUris: ["http://127.0.0.1:45678/callback", "cursor://callback"],
            GrantTypes: ["authorization_code", "refresh_token"],
            ResponseTypes: ["code"],
            TokenEndpointAuthMethod: "none");

        var response = await client.PostAsJsonAsync("/oauth/register", regRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<OAuthClientRegistrationResponse>(JsonOpts);
        result.Should().NotBeNull();
        result.ClientId.Should().StartWith("client_");
        result.ClientName.Should().Be("Test Desktop AI");
        result.RedirectUris.Should().Contain("http://127.0.0.1:45678/callback");
        result.RedirectUris.Should().Contain("cursor://callback");
    }

    [Fact]
    public async Task AuthorizationCodeFlow_WithPkce_IssuesTokenAndCanCallMcp()
    {
        var client = CreateNoRedirectClient();
        var (email, password) = await RegisterUserAsync(client);

        // Sign in with cookie
        var loginForm = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password
        };
        var loginRes = await client.PostAsync("/api/auth/cookie-login", new FormUrlEncodedContent(loginForm));
        loginRes.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // Prepare PKCE
        var verifier = "test-pkce-verifier-string-12345-very-long-and-secure-random-token";
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        const string redirectUri = "http://127.0.0.1:54321/callback";
        const string clientId = "claude-desktop";
        const string state = "random_state_xyz";

        // GET consent screen
        var consentRes = await client.GetAsync($"/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope=full&state={state}&code_challenge={challenge}&code_challenge_method=S256");
        consentRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var consentHtml = await consentRes.Content.ReadAsStringAsync();
        consentHtml.Should().Contain("Claude Desktop");
        consentHtml.Should().Contain("Full access");

        // POST consent approve
        var authPostForm = new Dictionary<string, string>
        {
            ["action"] = "approve",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "full",
            ["state"] = state,
            ["code_challenge"] = challenge
        };
        var authRes = await client.PostAsync("/oauth/authorize", new FormUrlEncodedContent(authPostForm));
        authRes.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var redirectLocation = authRes.Headers.Location;
        redirectLocation.Should().NotBeNull();
        redirectLocation.ToString().Should().StartWith(redirectUri);

        var query = QueryHelpers.ParseQuery(redirectLocation.Query);
        query.Should().ContainKey("code");
        query["state"].ToString().Should().Be(state);
        var code = query["code"].ToString();

        // Token exchange
        var tokenForm = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier
        };
        var tokenRes = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(tokenForm));
        tokenRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenPayload = await tokenRes.Content.ReadFromJsonAsync<OAuthTokenResponse>(JsonOpts);
        tokenPayload.Should().NotBeNull();
        tokenPayload.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokenPayload.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokenPayload.TokenType.Should().Be("Bearer");
        tokenPayload.Scope.Should().Be("full");

        // Call MCP using the OAuth access token
        var mcpClientHttp = factory.CreateClient();
        mcpClientHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenPayload.AccessToken);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(mcpClientHttp.BaseAddress!, "/mcp") },
            mcpClientHttp,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);
        await using var mcp = await McpClient.CreateAsync(transport);

        var summary = await mcp.CallToolAsync("get_board_summary", new Dictionary<string, object?>());
        summary.IsError.GetValueOrDefault().Should().BeFalse();
    }

    [Fact]
    public async Task AuthorizationCodeFlow_PkceVerifierMismatch_Fails()
    {
        var client = CreateNoRedirectClient();
        var (email, password) = await RegisterUserAsync(client);

        var loginForm = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password
        };
        await client.PostAsync("/api/auth/cookie-login", new FormUrlEncodedContent(loginForm));

        var verifier = "correct-verifier-string-1234567890-abcdef";
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        const string redirectUri = "http://127.0.0.1:54321/callback";
        const string clientId = "cursor";

        var authPostForm = new Dictionary<string, string>
        {
            ["action"] = "approve",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "full",
            ["code_challenge"] = challenge
        };
        var authRes = await client.PostAsync("/oauth/authorize", new FormUrlEncodedContent(authPostForm));
        authRes.Headers.Location.Should().NotBeNull();
        var query = QueryHelpers.ParseQuery(authRes.Headers.Location.Query);
        var code = query["code"].ToString();

        // Pass WRONG verifier
        var tokenForm = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = "wrong-verifier-1234567890-xyz"
        };
        var tokenRes = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(tokenForm));
        tokenRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errDoc = await tokenRes.Content.ReadFromJsonAsync<JsonElement>();
        errDoc.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task AuthorizationCodeFlow_ReadOnlyScope_BlocksMutations()
    {
        var client = CreateNoRedirectClient();
        var (email, password) = await RegisterUserAsync(client);

        var loginForm = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password
        };
        await client.PostAsync("/api/auth/cookie-login", new FormUrlEncodedContent(loginForm));

        var verifier = "readonly-verifier-string-1234567890-abcdef";
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        const string redirectUri = "http://127.0.0.1:54321/callback";
        const string clientId = "opencode";

        var authPostForm = new Dictionary<string, string>
        {
            ["action"] = "approve",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = McpTokenScopes.ReadOnly,
            ["code_challenge"] = challenge
        };
        var authRes = await client.PostAsync("/oauth/authorize", new FormUrlEncodedContent(authPostForm));
        authRes.Headers.Location.Should().NotBeNull();
        var code = QueryHelpers.ParseQuery(authRes.Headers.Location.Query)["code"].ToString();

        var tokenForm = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier
        };
        var tokenRes = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(tokenForm));
        tokenRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenPayload = await tokenRes.Content.ReadFromJsonAsync<OAuthTokenResponse>(JsonOpts);
        tokenPayload.Should().NotBeNull();
        tokenPayload.Scope.Should().Be(McpTokenScopes.ReadOnly);

        var mcpClientHttp = factory.CreateClient();
        mcpClientHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenPayload.AccessToken);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(mcpClientHttp.BaseAddress!, "/mcp") },
            mcpClientHttp,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);
        await using var mcp = await McpClient.CreateAsync(transport);

        // Read works
        var summary = await mcp.CallToolAsync("get_board_summary", new Dictionary<string, object?>());
        summary.IsError.GetValueOrDefault().Should().BeFalse();

        // Mutation is blocked
        var act = async () => await mcp.CallToolAsync("create_item", new Dictionary<string, object?>
        {
            ["section"] = "todo",
            ["title"] = "Should Fail"
        });
        await act.Should().ThrowAsync<McpProtocolException>()
            .WithMessage("*read_only*");
    }

    [Fact]
    public async Task RefreshToken_RotatesAndMaintainsAccess()
    {
        var client = CreateNoRedirectClient();
        var (email, password) = await RegisterUserAsync(client);

        var loginForm = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password
        };
        await client.PostAsync("/api/auth/cookie-login", new FormUrlEncodedContent(loginForm));

        var verifier = "refresh-verifier-string-1234567890-abcdef";
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        const string redirectUri = "http://127.0.0.1:54321/callback";
        const string clientId = "claude-desktop";

        var authPostForm = new Dictionary<string, string>
        {
            ["action"] = "approve",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "full",
            ["code_challenge"] = challenge
        };
        var authRes = await client.PostAsync("/oauth/authorize", new FormUrlEncodedContent(authPostForm));
        authRes.Headers.Location.Should().NotBeNull();
        var code = QueryHelpers.ParseQuery(authRes.Headers.Location.Query)["code"].ToString();

        var tokenForm = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier
        };
        var tokenRes = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(tokenForm));
        var tokenPayload = await tokenRes.Content.ReadFromJsonAsync<OAuthTokenResponse>(JsonOpts);
        tokenPayload.Should().NotBeNull();

        // Refresh the token
        var refreshForm = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokenPayload.RefreshToken!,
            ["client_id"] = clientId
        };
        var refreshRes = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(refreshForm));
        refreshRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshedPayload = await refreshRes.Content.ReadFromJsonAsync<OAuthTokenResponse>(JsonOpts);
        refreshedPayload.Should().NotBeNull();
        refreshedPayload.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshedPayload.RefreshToken.Should().NotBe(tokenPayload.RefreshToken);

        // Verify the old refresh token can no longer be used
        var oldRefreshRes = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(refreshForm));
        oldRefreshRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Verify new access token works on /mcp
        var mcpClientHttp = factory.CreateClient();
        mcpClientHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshedPayload.AccessToken);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(mcpClientHttp.BaseAddress!, "/mcp") },
            mcpClientHttp,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);
        await using var mcp = await McpClient.CreateAsync(transport);

        var summary = await mcp.CallToolAsync("get_board_summary", new Dictionary<string, object?>());
        summary.IsError.GetValueOrDefault().Should().BeFalse();
    }

    private static async Task<(string Email, string Password)> RegisterUserAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"oauth-{suffix}@integration.test";
        const string password = "TestUser1!Aa";

        var registerRes = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, password));
        registerRes.EnsureSuccessStatusCode();

        return (email, password);
    }
}
