using FluentAssertions;

using Microsoft.Playwright;

namespace App.Web.E2E;

/// <summary>Browser smoke tests. Requires a running web app. See README and CI, set E2E_BASE_URL.</summary>
public sealed class WebSmokeTests
{
    private static string BaseUrl => WebE2E.BaseUrl;

    private static Task EnsureBaseUrlReachableAsync() => WebE2E.EnsureBaseUrlReachableAsync();

    [SkippableFact]
    public async Task Home_loads_without_server_error()
    {
        await EnsureBaseUrlReachableAsync();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var res = await page.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        res.Should().NotBeNull();
        res.Ok.Should().BeTrue(
            $"Home returned HTTP {res.Status} for {BaseUrl}/ - start App.Web (see README / CI job).");
        (await page.ContentAsync()).Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task Login_page_reachable()
    {
        await EnsureBaseUrlReachableAsync();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var res = await page.GotoAsync($"{BaseUrl}/auth/login",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        res.Should().NotBeNull();
        res.Ok.Should().BeTrue($"Login page status {res.Status}. Base: {BaseUrl}");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Sign in", new() { IgnoreCase = true });
    }

    [SkippableFact]
    public async Task Register_page_reachable()
    {
        await EnsureBaseUrlReachableAsync();
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var res = await page.GotoAsync($"{BaseUrl}/auth/register",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        res.Should().NotBeNull();
        res.Ok.Should().BeTrue($"Register page status {res.Status}. Base: {BaseUrl}");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Create account", new() { IgnoreCase = true });
    }
}
