using System.Text.RegularExpressions;
using System.Net.Http;
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

    private static async Task EnsureBaseUrlReachableAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var res = await client.GetAsync($"{BaseUrl}/health", cts.Token);
            if (res.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch
        {
            // Ignore and fall through to skip.
        }

        throw new Xunit.Sdk.SkipException(
            $"E2E_BASE_URL '{BaseUrl}' is not reachable. Start App.Web before running Playwright tests.");
    }

    [Fact]
    public async Task Capture_key_pages_as_png()
    {
        await EnsureBaseUrlReachableAsync();
        Directory.CreateDirectory(ScreenshotDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } });

        await page.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByText("Welcome to Habitinator").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForTimeoutAsync(400);

        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("demo guest", RegexOptions.IgnoreCase) })
            .ClickAsync();

        await page.WaitForURLAsync(u => u.StartsWith($"{BaseUrl}/", StringComparison.OrdinalIgnoreCase) &&
                                        !u.Contains("/auth/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 60_000 });

        await DismissCatchUpDialogIfPresentAsync(page);

        await page.Locator(".board-shell").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "02-board.png"), FullPage = true });

        await page.Locator(".habitica-column--daily").GetByRole(AriaRole.Button, new() { Name = "All" }).ClickAsync();
        await page.WaitForTimeoutAsync(200);
        await page.Locator(".habitica-column--daily .habitica-card__title").First.ClickAsync();
        await page.Locator(".edit-daily-dialog").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForTimeoutAsync(400);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "03-edit-daily.png"), FullPage = false });

        await page.Locator(".edit-daily-dialog").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await page.Locator(".edit-daily-dialog").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.GotoAsync($"{BaseUrl}/stats", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "04-statistics.png"), FullPage = true });

        var heatmapWithActivity = page.Locator("button.stats-heatmap-day-btn:not(.stats-lvl-0)");
        if (await heatmapWithActivity.CountAsync() > 0)
        {
            await heatmapWithActivity.First.ClickAsync();
        }
        else
        {
            await page.Locator("button.stats-heatmap-day-btn").First.ClickAsync();
        }

        await page.Locator(".activity-day-detail-dialog").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForTimeoutAsync(1000);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "05-activity-day-detail.png"), FullPage = false });

        await page.Locator(".activity-day-detail-dialog").GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await page.Locator(".activity-day-detail-dialog").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        await page.GotoAsync($"{BaseUrl}/settings", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "06-settings.png"), FullPage = true });
    }

    [Fact]
    public async Task Capture_welcome_login_register_pages()
    {
        await EnsureBaseUrlReachableAsync();
        Directory.CreateDirectory(ScreenshotDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        // Create a new context (isolated session) for clean state
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } });
        var page = await context.NewPageAsync();

        // Welcome page (not logged in)
        await page.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByText("Welcome to Habitinator").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "01-welcome.png"), FullPage = true });

        // Login page
        await page.GotoAsync($"{BaseUrl}/auth/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByRole(AriaRole.Heading, new() { Name = "Login" }).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "07-login.png"), FullPage = true });

        // Register page
        await page.GotoAsync($"{BaseUrl}/auth/register", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByRole(AriaRole.Heading, new() { Name = "Register" }).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "08-register.png"), FullPage = true });
    }

    [Fact]
    public async Task Capture_all_modals()
    {
        await EnsureBaseUrlReachableAsync();
        Directory.CreateDirectory(ScreenshotDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } });

        // Login first
        await page.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByText("Welcome to Habitinator").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("demo guest", RegexOptions.IgnoreCase) })
            .ClickAsync();
        await page.WaitForURLAsync(u => u.StartsWith($"{BaseUrl}/", StringComparison.OrdinalIgnoreCase) &&
                                        !u.Contains("/auth/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 60_000 });

        await DismissCatchUpDialogIfPresentAsync(page);
        await page.Locator(".board-shell").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        await page.WaitForTimeoutAsync(500);

        // Edit Habit modal
        await page.Locator(".habitica-column--habit .habitica-card__title").First.ClickAsync();
        await page.Locator(".edit-habit-dialog").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForTimeoutAsync(400);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "09-edit-habit.png"), FullPage = false });
        await page.Locator(".edit-habit-dialog").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await page.Locator(".edit-habit-dialog").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        // Edit Todo modal
        await page.Locator(".habitica-column--todo .habitica-card__title").First.ClickAsync();
        await page.Locator(".edit-daily-dialog").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForTimeoutAsync(400);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "10-edit-todo.png"), FullPage = false });
        await page.Locator(".edit-daily-dialog").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await page.Locator(".edit-daily-dialog").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    [Fact]
    public async Task Capture_yesterday_checkin_modal()
    {
        await EnsureBaseUrlReachableAsync();
        Directory.CreateDirectory(ScreenshotDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } });

        // Login first
        await page.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByText("Welcome to Habitinator").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("demo guest", RegexOptions.IgnoreCase) })
            .ClickAsync();
        await page.WaitForURLAsync(u => u.StartsWith($"{BaseUrl}/", StringComparison.OrdinalIgnoreCase) &&
                                        !u.Contains("/auth/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 60_000 });

        // Wait for board to load - we need to keep the yesterday dialog open
        await page.Locator(".board-shell").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        await page.WaitForTimeoutAsync(500);

        // Check if yesterday retro dialog appears
        var yesterdayDialog = page.Locator(".daily-yesterday-dialog");
        try
        {
            await yesterdayDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await page.WaitForTimeoutAsync(500);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "11-yesterday-checkin.png"), FullPage = false });

            // Close it
            await yesterdayDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
            await yesterdayDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        }
        catch (Exception)
        {
            // Dialog might not appear if already dismissed today or no missed dailies
            // Take screenshot of board anyway to indicate modal was not available
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "11-yesterday-checkin-skipped.png"), FullPage = true });
        }
    }

    [Fact]
    public async Task Capture_timer_times_up_modal()
    {
        await EnsureBaseUrlReachableAsync();
        Directory.CreateDirectory(ScreenshotDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } });

        // Login first
        await page.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByText("Welcome to Habitinator").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("demo guest", RegexOptions.IgnoreCase) })
            .ClickAsync();
        await page.WaitForURLAsync(u => u.StartsWith($"{BaseUrl}/", StringComparison.OrdinalIgnoreCase) &&
                                        !u.Contains("/auth/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 60_000 });

        await DismissCatchUpDialogIfPresentAsync(page);
        await page.Locator(".board-shell").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        await page.WaitForTimeoutAsync(500);

        // Open the session target dropdown and select first habit
        await page.Locator(".timer-target-field input").ClickAsync();
        await page.WaitForTimeoutAsync(500);
        // Press Down arrow to open dropdown and then Enter to select first item
        await page.Locator(".timer-target-field input").PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(300);
        await page.Locator(".timer-target-field input").PressAsync("Enter");
        await page.WaitForTimeoutAsync(500);

        // Set time's up after to 1 second and press Enter to apply
        await page.Locator(".timer-focus-textfield input").FillAsync("1s");
        await page.Locator(".timer-focus-textfield input").PressAsync("Enter");
        await page.WaitForTimeoutAsync(500);

        // Wait for Start button to be enabled and click it
        await page.Locator(".timer-btn--start:not([disabled])").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Locator(".timer-btn--start").ClickAsync();

        // Wait for the timer to reach 1 second and the modal to appear (give it 3 seconds to be safe)
        await page.WaitForTimeoutAsync(3000);

        // The times up modal should now be visible - it's a MudBlazor MessageBox
        var timesUpDialog = page.Locator(".mud-message-box");
        await timesUpDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Screenshot the modal
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ScreenshotDir, "12-timer-times-up.png"), FullPage = false });

        // Dismiss the dialog by clicking "Not done" to resume
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("not done", RegexOptions.IgnoreCase) }).ClickAsync();
        await timesUpDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        // Pause the timer first, then reset (reset requires timer to not be running)
        await page.Locator(".timer-btn--pause").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Wait for reset button to be enabled and click it
        await page.Locator(".timer-btn--reset:not([disabled])").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Locator(".timer-btn--reset").ClickAsync();
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
