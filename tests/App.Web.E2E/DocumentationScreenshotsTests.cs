using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace App.Web.E2E;

/// <summary>
/// Captures PNG screenshots for documentation. Requires running web app (see CI / E2E_BASE_URL).
/// Output: E2E_SCREENSHOT_DIR or %TEMP%/habitinator-e2e-screenshots
/// </summary>
public sealed class DocumentationScreenshotsTests
{
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL")?.TrimEnd('/')
        ?? "http://127.0.0.1:5050";

    private static string ScreenshotDir =>
        Environment.GetEnvironmentVariable("E2E_SCREENSHOT_DIR")?.Trim()
        ?? Path.Combine(Path.GetTempPath(), "habitinator-e2e-screenshots");

    [Fact]
    public async Task Capture_key_pages_as_png()
    {
        Directory.CreateDirectory(ScreenshotDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } });

        await page.GotoAsync($"{BaseUrl}/auth/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "01-login.png"), FullPage = false });

        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("demo guest", RegexOptions.IgnoreCase) })
            .ClickAsync();

        await page.WaitForURLAsync(u => u.StartsWith($"{BaseUrl}/", StringComparison.OrdinalIgnoreCase) &&
                                        !u.Contains("/auth/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 60_000 });

        await DismissCatchUpDialogIfPresentAsync(page);

        await page.Locator(".board-shell").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "02-board.png"), FullPage = true });

        await page.GotoAsync($"{BaseUrl}/stats", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "03-statistics.png"), FullPage = true });

        await page.GotoAsync($"{BaseUrl}/settings", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "04-settings.png"), FullPage = true });
    }

    private static async Task DismissCatchUpDialogIfPresentAsync(IPage page)
    {
        var dialog = page.Locator(".daily-yesterday-dialog");
        try
        {
            await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
        }
        catch (Exception)
        {
            return;
        }

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync(new() { Timeout = 10_000 });
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }
}
