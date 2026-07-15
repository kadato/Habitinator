using System.Text.RegularExpressions;
using System.Net.Http;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace App.Web.E2E;

/// <summary>
/// Captures PNG screenshots for documentation in light and dark mode (via browser color scheme).
/// Requires running web app (see CI / E2E_BASE_URL).
/// Output: E2E_SCREENSHOT_DIR or %TEMP%/habitinator-e2e-screenshots
/// Files are named {base}-{light|dark}.png (e.g. 02-board-light.png).
/// </summary>
public sealed class DocumentationScreenshotsTests
{
    private static readonly (string Suffix, ColorScheme Scheme)[] ThemeCaptureSchemes =
    [
        ("light", ColorScheme.Light),
        ("dark", ColorScheme.Dark),
    ];

    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL")?.TrimEnd('/')
        ?? "http://127.0.0.1:5050";

    private static string ScreenshotDir =>
        Environment.GetEnvironmentVariable("E2E_SCREENSHOT_DIR")?.Trim()
        ?? Path.Combine(Path.GetTempPath(), "habitinator-e2e-screenshots");

    private static string ShotPath(string baseFileName, string schemeSuffix) =>
        Path.Combine(ScreenshotDir, $"{baseFileName}-{schemeSuffix}.png");


    private static BrowserNewContextOptions CreateContextOptions(ColorScheme scheme) =>
        new()
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
            ColorScheme = scheme
        };

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
    public async Task Capture_key_pages_as_png()
    {
        await EnsureBaseUrlReachableAsync();
        Directory.CreateDirectory(ScreenshotDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        foreach (var (suffix, scheme) in ThemeCaptureSchemes)
        {
            await using var context = await browser.NewContextAsync(CreateContextOptions(scheme));
            var page = await context.NewPageAsync();

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
            var boardPath = ShotPath("02-board", suffix);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = boardPath, FullPage = true });

            await page.Locator(".board-column--daily").GetByRole(AriaRole.Button, new() { Name = "All" }).ClickAsync();
            await page.WaitForTimeoutAsync(200);
            await page.Locator(".board-column--daily .board-card__title").First.ClickAsync();
            await page.Locator(".edit-daily-dialog").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            await page.WaitForTimeoutAsync(400);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("03-edit-daily", suffix), FullPage = false });

            await page.Locator(".edit-daily-dialog").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
            await page.Locator(".edit-daily-dialog").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

            await page.GotoAsync($"{BaseUrl}/stats", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.WaitForTimeoutAsync(500);
            var statsPath = ShotPath("04-statistics", suffix);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = statsPath, FullPage = true });

            await ClickBusiestHeatmapDayAsync(page);

            await page.Locator(".activity-day-detail-dialog").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            await page.WaitForTimeoutAsync(1000);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("05-activity-day-detail", suffix), FullPage = false });

            await page.Locator(".activity-day-detail-dialog").GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
            await page.Locator(".activity-day-detail-dialog").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

            await page.GotoAsync($"{BaseUrl}/settings", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.WaitForTimeoutAsync(500);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("06-settings", suffix), FullPage = true });
        }
    }

    [Fact]
    public async Task Capture_welcome_login_register_pages()
    {
        await EnsureBaseUrlReachableAsync();
        Directory.CreateDirectory(ScreenshotDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        foreach (var (suffix, scheme) in ThemeCaptureSchemes)
        {
            await using var context = await browser.NewContextAsync(CreateContextOptions(scheme));
            var page = await context.NewPageAsync();

            await page.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByText("Welcome to Habitinator").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            await page.WaitForTimeoutAsync(500);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("01-welcome", suffix), FullPage = true });

            await page.GotoAsync($"{BaseUrl}/auth/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" }).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            await page.WaitForTimeoutAsync(500);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("07-login", suffix), FullPage = true });

            await page.GotoAsync($"{BaseUrl}/auth/register", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByRole(AriaRole.Heading, new() { Name = "Create account" }).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            await page.WaitForTimeoutAsync(500);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("08-register", suffix), FullPage = true });
        }
    }

    [Fact]
    public async Task Capture_all_modals()
    {
        await EnsureBaseUrlReachableAsync();
        Directory.CreateDirectory(ScreenshotDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        foreach (var (suffix, scheme) in ThemeCaptureSchemes)
        {
            await using var context = await browser.NewContextAsync(CreateContextOptions(scheme));
            var page = await context.NewPageAsync();

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

            await page.Locator(".board-columns-desktop .board-column--habit .board-card__title").First.ClickAsync();
            await page.Locator(".edit-habit-dialog").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            await page.WaitForTimeoutAsync(400);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("09-edit-habit", suffix), FullPage = false });
            await page.Locator(".edit-habit-dialog").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
            await page.Locator(".edit-habit-dialog").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

            await page.Locator(".board-columns-desktop .board-column--todo .board-card__title").First.ClickAsync();
            await page.Locator(".edit-daily-dialog").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            await page.WaitForTimeoutAsync(400);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("10-edit-todo", suffix), FullPage = false });
            await page.Locator(".edit-daily-dialog").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
            await page.Locator(".edit-daily-dialog").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        }
    }

    [Fact]
    public async Task Capture_yesterday_checkin_modal()
    {
        await EnsureBaseUrlReachableAsync();
        Directory.CreateDirectory(ScreenshotDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        foreach (var (suffix, scheme) in ThemeCaptureSchemes)
        {
            await using var context = await browser.NewContextAsync(CreateContextOptions(scheme));
            var page = await context.NewPageAsync();

            await page.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByText("Welcome to Habitinator").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("demo guest", RegexOptions.IgnoreCase) })
                .ClickAsync();
            await page.WaitForURLAsync(u => u.StartsWith($"{BaseUrl}/", StringComparison.OrdinalIgnoreCase) &&
                                            !u.Contains("/auth/login", StringComparison.OrdinalIgnoreCase),
                new() { Timeout = 60_000 });

            await page.Locator(".board-shell").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
            await page.WaitForTimeoutAsync(500);

            var yesterdayDialog = page.Locator(".daily-yesterday-dialog");
            try
            {
                await yesterdayDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
                await page.WaitForTimeoutAsync(500);
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("11-yesterday-checkin", suffix), FullPage = false });

                await yesterdayDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
                await yesterdayDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
            }
            catch (Exception)
            {
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("11-yesterday-checkin-skipped", suffix), FullPage = true });
            }
        }
    }

    [Fact]
    public async Task Capture_timer_times_up_modal()
    {
        await EnsureBaseUrlReachableAsync();
        Directory.CreateDirectory(ScreenshotDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        foreach (var (suffix, scheme) in ThemeCaptureSchemes)
        {
            await using var context = await browser.NewContextAsync(CreateContextOptions(scheme));
            var page = await context.NewPageAsync();

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

            await page.Locator(".timer-target-field input").ClickAsync();
            await page.WaitForTimeoutAsync(500);
            await page.Locator(".timer-target-field input").PressAsync("ArrowDown");
            await page.WaitForTimeoutAsync(300);
            await page.Locator(".timer-target-field input").PressAsync("Enter");
            await page.WaitForTimeoutAsync(500);

            await page.Locator(".timer-focus-textfield input").FillAsync("1s");
            await page.Locator(".timer-focus-textfield input").PressAsync("Enter");
            await page.WaitForTimeoutAsync(500);

            await page.Locator(".timer-btn--start:not([disabled])").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await page.Locator(".timer-btn--start").ClickAsync();

            await page.WaitForTimeoutAsync(3000);

            var timesUpDialog = page.Locator(".mud-message-box");
            await timesUpDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

            await page.ScreenshotAsync(new PageScreenshotOptions { Path = ShotPath("12-timer-times-up", suffix), FullPage = false });

            await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("not done", RegexOptions.IgnoreCase) }).ClickAsync();
            await timesUpDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

            await page.Locator(".timer-btn--pause").ClickAsync();
            await page.WaitForTimeoutAsync(300);

        try
        {
            await page.Locator(".timer-btn--reset:not([disabled])").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            await page.Locator(".timer-btn--reset").ClickAsync();
        }
        catch (TimeoutException)
        {
            // Screenshot already captured; reset is best-effort cleanup.
        }
        }
    }

    /// <summary>Clicks the heatmap day with the highest event count (from button title).</summary>
    private static async Task ClickBusiestHeatmapDayAsync(IPage page)
    {
        var clicked = await page.EvaluateAsync<bool>(
            """
            () => {
                const buttons = Array.from(
                    document.querySelectorAll('button.stats-heatmap-day-btn:not(.stats-lvl-0):not(.stats-lvl-na)'));
                let best = null;
                let bestCount = -1;
                for (const btn of buttons) {
                    const match = (btn.getAttribute('title') || '').match(/: (\d+) event/);
                    const count = match ? parseInt(match[1], 10) : 0;
                    if (count > bestCount) {
                        bestCount = count;
                        best = btn;
                    }
                }
                if (best) {
                    best.click();
                    return true;
                }
                return false;
            }
            """);

        if (!clicked)
        {
            for (var level = 4; level >= 1; level--)
            {
                var byLevel = page.Locator($"button.stats-heatmap-day-btn.stats-lvl-{level}");
                if (await byLevel.CountAsync() > 0)
                {
                    await byLevel.First.ClickAsync();
                    return;
                }
            }

            await page.Locator("button.stats-heatmap-day-btn").First.ClickAsync();
        }
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

