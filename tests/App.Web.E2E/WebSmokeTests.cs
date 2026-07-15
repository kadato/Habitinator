using Microsoft.Playwright;
using System.Net.Http;

using FluentAssertions;

namespace App.Web.E2E;

/// <summary>Browser smoke tests. Requires a running web app (see README / CI: set E2E_BASE_URL).</summary>
public sealed class WebSmokeTests
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL")?.TrimEnd('/')
        ?? "http://127.0.0.1:5050";

    private static bool? _isBaseUrlReachable;

    private static async Task EnsureBaseUrlReachableAsync()
    {
        if (_isBaseUrlReachable.HasValue)
        {
            if (!_isBaseUrlReachable.Value)
            {
                throw Xunit.Sdk.SkipException.ForSkip(
                    $"E2E_BASE_URL '{BaseUrl}' is not reachable (cached check).");
            }
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var res = await client.GetAsync($"{BaseUrl}/health", cts.Token);
            if (res.IsSuccessStatusCode)
            {
                _isBaseUrlReachable = true;
                return;
            }
        }
        catch
        {
            // Ignore and fall through to skip.
        }

        _isBaseUrlReachable = false;
        throw Xunit.Sdk.SkipException.ForSkip(
            $"E2E_BASE_URL '{BaseUrl}' is not reachable. Start App.Web before running Playwright tests.");
    }

    [Fact]
    public async Task Home_loads_without_server_error()
    {
        await EnsureBaseUrlReachableAsync();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var res = await page.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        res.Should().NotBeNull();
        res!.Ok.Should().BeTrue(
            $"Home returned HTTP {(int)res.Status} for {BaseUrl}/ — start App.Web (see README / CI job).");
        (await page.ContentAsync()).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_page_reachable()
    {
        await EnsureBaseUrlReachableAsync();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var res = await page.GotoAsync($"{BaseUrl}/auth/login",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        res.Should().NotBeNull();
        res!.Ok.Should().BeTrue($"Login page status {res.Status}. Base: {BaseUrl}");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("login", new() { IgnoreCase = true });
    }

    [Fact]
    public async Task Register_page_reachable()
    {
        await EnsureBaseUrlReachableAsync();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var res = await page.GotoAsync($"{BaseUrl}/auth/register",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        res.Should().NotBeNull();
        res!.Ok.Should().BeTrue($"Register page status {res.Status}. Base: {BaseUrl}");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("register", new() { IgnoreCase = true });
    }
}
