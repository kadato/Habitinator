using App.Web.Middleware;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace App.Web.IntegrationTests;

/// <summary>Unit tests for the CSP connect-src policy. No database or host is needed.</summary>
public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task Development_allows_the_wasm_debugger_source_host()
    {
        var context = await InvokeAsync(Environments.Development, "localhost:5033");

        var csp = context.Response.Headers.ContentSecurityPolicy.ToString();
        csp.Should().Contain("https://raw.githubusercontent.com");
        csp.Should().Contain("wss://localhost:5033");
        csp.Should().NotContain("connect-src 'self' https:");
    }

    [Fact]
    public async Task Production_limits_connect_src_to_the_app_host()
    {
        var context = await InvokeAsync(Environments.Production, "habitinator.app");

        var csp = context.Response.Headers.ContentSecurityPolicy.ToString();
        csp.Should().Contain("connect-src 'self' wss://habitinator.app ws://habitinator.app;");
        csp.Should().NotContain("raw.githubusercontent.com");
        csp.Should().NotContain(" https: ");
        csp.Should().NotContain(" wss: ");
    }

    private static async Task<HttpContext> InvokeAsync(string environmentName, string host)
    {
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, new FakeEnvironment(environmentName));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        await middleware.InvokeAsync(context);
        return context;
    }

    private sealed class FakeEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Habitinator.Tests";

        public string WebRootPath { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
