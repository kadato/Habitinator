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
    [InlineData("/.well-known/security.txt")]
    [InlineData("/.well-known/api-catalog")]
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
    }

    [Fact]
    public async Task Unknown_path_returns_not_found_status()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/path-does-not-exist-website-spec-test");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
