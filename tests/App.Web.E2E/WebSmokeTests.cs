using Microsoft.Playwright;

namespace App.Web.E2E;

/// <summary>Browser smoke tests. Requires a running web app (see README / CI: set E2E_BASE_URL).</summary>
public sealed class WebSmokeTests
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL")?.TrimEnd('/')
        ?? "http://127.0.0.1:5050";

    [Fact]
    public async Task Home_loads_without_server_error()
    {
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var res = await page.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.NotNull(res);
        Assert.True(res.Ok,
            $"Home returned HTTP {(int)res.Status} for {BaseUrl}/ — start App.Web (see README / CI job).");
        Assert.False(string.IsNullOrWhiteSpace(await page.ContentAsync()));
    }

    [Fact]
    public async Task Login_page_reachable()
    {
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var res = await page.GotoAsync($"{BaseUrl}/auth/login",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.NotNull(res);
        Assert.True(res.Ok, $"Login page status {res.Status}. Base: {BaseUrl}");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("login", new() { IgnoreCase = true });
    }

    [Fact]
    public async Task Register_page_reachable()
    {
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var res = await page.GotoAsync($"{BaseUrl}/auth/register",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.NotNull(res);
        Assert.True(res.Ok, $"Register page status {res.Status}. Base: {BaseUrl}");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("register", new() { IgnoreCase = true });
    }
}
