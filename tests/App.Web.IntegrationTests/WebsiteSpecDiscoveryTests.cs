using System.Net;

using FluentAssertions;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class WebsiteSpecDiscoveryTests(PostgresWebAppFactory factory)
{
    [Theory]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    [InlineData("/llms.txt")]
    [InlineData("/auth.md")]
    [InlineData("/.well-known/security.txt")]
    [InlineData("/.well-known/api-catalog")]
    [InlineData("/.well-known/mcp/server-card.json")]
    [InlineData("/.well-known/ai-catalog.json")]
    [InlineData("/.well-known/agent-skills/index.json")]
    [InlineData("/.well-known/agent-skills/habitinator/SKILL.md")]
    public async Task Discovery_files_are_available(string path)
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync(path);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Change_password_well_known_redirects_to_settings()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var res = await client.GetAsync("/.well-known/change-password");
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().EndWith("/settings");
    }

    [Fact]
    public async Task Security_headers_are_present_on_html_shell()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/");
        res.Headers.TryGetValues("X-Content-Type-Options", out var nosniff).Should().BeTrue();
        nosniff!.First().Should().Be("nosniff");
        res.Headers.TryGetValues("Referrer-Policy", out var referrer).Should().BeTrue();
        referrer!.First().Should().Be("strict-origin-when-cross-origin");
        res.Headers.TryGetValues("Content-Security-Policy", out var csp).Should().BeTrue();
        csp!.First().Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task Discovery_link_headers_are_present_on_html()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Accept.ParseAdd("text/html");
        var res = await client.SendAsync(request);
        res.Headers.TryGetValues("Link", out var links).Should().BeTrue();
        var combined = string.Join(" ", links!);
        combined.Should().Contain("/openapi/v1.json");
        combined.Should().Contain("/.well-known/api-catalog");
        combined.Should().Contain("/sitemap.xml");
        combined.Should().Contain("/llms.txt");
        combined.Should().Contain("/.well-known/mcp/server-card.json");
        combined.Should().Contain("rel=\"mcp\"");
        combined.Should().Contain("/.well-known/ai-catalog.json");
        combined.Should().Contain("/.well-known/agent-skills/index.json");
    }

    [Fact]
    public async Task Server_card_lists_tools_and_prompts()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/.well-known/mcp/server-card.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("\"habitinator\"");
        body.Should().Contain("/mcp");
        body.Should().Contain("get_board_summary");
        body.Should().Contain("plan_day");
    }

    [Fact]
    public async Task Agent_skills_index_digest_matches_skill_file()
    {
        var client = factory.CreateClient();
        var indexJson = await client.GetStringAsync("/.well-known/agent-skills/index.json");
        var skill = await client.GetByteArrayAsync("/.well-known/agent-skills/habitinator/SKILL.md");
        var digest = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(skill)).ToLowerInvariant();
        indexJson.Should().Contain(digest);
    }

    [Fact]
    public async Task Ai_catalog_points_at_server_card()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/.well-known/ai-catalog.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("/.well-known/mcp/server-card.json");
        body.Should().Contain("application/mcp-server-card+json");
    }

    [Fact]
    public async Task Markdown_discovery_files_serve_markdown_content_type()
    {
        var client = factory.CreateClient();
        foreach (var path in new[] { "/auth.md", "/.well-known/agent-skills/habitinator/SKILL.md" })
        {
            var res = await client.GetAsync(path);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            res.Content.Headers.ContentType?.MediaType.Should().Be("text/markdown");
        }
    }

    [Fact]
    public async Task Api_catalog_serves_linkset_content_type()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/.well-known/api-catalog");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType?.MediaType.Should().Be("application/linkset+json");
    }

    [Fact]
    public async Task Landing_page_contains_structured_data()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("schema.org");
        body.Should().Contain("WebApplication");
    }

    [Fact]
    public async Task Api_catalog_advertises_mcp_endpoint()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/.well-known/api-catalog");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("/mcp");
    }

    [Fact]
    public async Task Llms_txt_advertises_mcp_endpoint()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/llms.txt");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("/mcp");
    }

    [Fact]
    public async Task Unknown_path_returns_not_found_status()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/path-does-not-exist-website-spec-test");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
