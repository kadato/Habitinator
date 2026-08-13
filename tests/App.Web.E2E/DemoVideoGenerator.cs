using System.Text.RegularExpressions;

using Microsoft.Playwright;

namespace App.Web.E2E;

/// <summary>
/// Automatically records a video walkthrough of the app showing the basic features.
/// Requires running web app (set E2E_BASE_URL).
/// Output: docs/automation/demo-video-dark.webm, docs/automation/demo-video-light.webm
/// </summary>
public sealed class DemoVideoGenerator
{
    private static readonly (string Suffix, ColorScheme Scheme)[] ThemeVideoSchemes =
    [
        ("dark", ColorScheme.Dark),
        ("light", ColorScheme.Light),
    ];

    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL")?.TrimEnd('/')
        ?? "http://127.0.0.1:5050";

    private static string VideoDir =>
        Environment.GetEnvironmentVariable("E2E_VIDEO_DIR")?.Trim()
        ?? Path.Combine(Path.GetTempPath(), "habitinator-e2e-videos");

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

    private static async Task CreateAuthStateAsync(IBrowser browser, string statePath)
    {
        var loginContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        var loginPage = await loginContext.NewPageAsync();
        await loginPage.GotoAsync($"{BaseUrl}/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await loginPage.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("demo guest", RegexOptions.IgnoreCase) })
            .ClickAsync();
        await loginPage.WaitForURLAsync(u => u.StartsWith($"{BaseUrl}/", StringComparison.OrdinalIgnoreCase) &&
                                        !u.Contains("/auth/login", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 30_000 });
        await loginPage.Locator(".board-shell").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await loginContext.StorageStateAsync(new() { Path = statePath });
        await loginContext.CloseAsync();
    }

    [Fact]
    public async Task Generate_Demo_Video()
    {
        await EnsureBaseUrlReachableAsync();
        Directory.CreateDirectory(VideoDir);

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        // Step A: Perform login in a temporary, unrecorded context to bypass login in the video
        var statePath = Path.Combine(Path.GetTempPath(), "habitinator-auth-state.json");
        await CreateAuthStateAsync(browser, statePath);
        // Loop over both Light and Dark themes
        foreach (var (suffix, scheme) in ThemeVideoSchemes)
        {
            // Step B: Set up recording context using the saved logged-in state
            var contextOptions = new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
                ColorScheme = scheme,
                RecordVideoDir = VideoDir,
                RecordVideoSize = new RecordVideoSize { Width = 1280, Height = 720 },
                StorageStatePath = statePath
            };

            await using var context = await browser.NewContextAsync(contextOptions);
            var page = await context.NewPageAsync();

            // 1. Load the Board directly (starts the video on the board already logged in)
            await page.GotoAsync($"{BaseUrl}/?theme={suffix}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await DismissCatchUpDialogIfPresentAsync(page);
            await page.Locator(".board-shell").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
            await page.WaitForTimeoutAsync(1000); // brief pause to settle

            // Smooth scroll down fully on Board
            await SmoothScrollAsync(page, down: true);
            await page.WaitForTimeoutAsync(800);

            // Smooth scroll up fully on Board
            await SmoothScrollAsync(page, down: false);
            await page.WaitForTimeoutAsync(800);

            // 2. Increment Habit card (800ms delay)
            var firstHabitPlus = page.Locator(".board-column--habit .board-card .board-card__actions button").First;
            if (await firstHabitPlus.CountAsync() > 0)
            {
                await firstHabitPlus.ScrollIntoViewIfNeededAsync();
                await firstHabitPlus.ClickAsync();
                await page.WaitForTimeoutAsync(800);
            }

            // 3. Open a Daily item detail/edit dialog (1.2s delay)
            var dailyTitle = page.Locator(".board-column--daily .board-card__title").First;
            await dailyTitle.ScrollIntoViewIfNeededAsync();
            await dailyTitle.ClickAsync();
            var dailyDialog = page.Locator(".edit-daily-dialog");
            await dailyDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await page.WaitForTimeoutAsync(1200);

            // Close it (800ms delay)
            await dailyDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
            await dailyDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
            await page.WaitForTimeoutAsync(800);

            // 4. Start Focus Timer (Timer section)
            var targetInput = page.Locator(".timer-target-field input");
            await targetInput.ClickAsync();
            await page.WaitForTimeoutAsync(300);
            await targetInput.PressAsync("ArrowDown");
            await page.WaitForTimeoutAsync(300);
            await targetInput.PressAsync("Enter");
            await page.WaitForTimeoutAsync(500);

            // Set focus duration to 3 seconds for a faster demo
            var focusInput = page.Locator(".timer-focus-textfield input");
            await focusInput.FillAsync("3s");
            await focusInput.PressAsync("Enter");
            await page.WaitForTimeoutAsync(500);

            // Click Start
            var startBtn = page.Locator(".timer-btn--start:not([disabled])");
            await startBtn.ClickAsync();

            // Wait for it to tick and show Time's Up
            await page.WaitForTimeoutAsync(4500);

            // Dismiss Time's up modal
            var timesUpDialog = page.Locator(".mud-message-box");
            await timesUpDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await page.WaitForTimeoutAsync(1000);
            await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("not done", RegexOptions.IgnoreCase) }).ClickAsync();
            await timesUpDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
            await page.WaitForTimeoutAsync(800);

            // 5. Navigate to Statistics page (1s delay + scroll down fully + scroll up fully)
            await page.GotoAsync($"{BaseUrl}/stats?theme={suffix}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.WaitForTimeoutAsync(1000);

            // Smooth scroll down fully to showcase all charts/summaries
            await SmoothScrollAsync(page, down: true);
            await page.WaitForTimeoutAsync(800);

            // Smooth scroll back up fully
            await SmoothScrollAsync(page, down: false);
            await page.WaitForTimeoutAsync(800);

            // Click busiest heatmap day to open modal
            await ClickBusiestHeatmapDayAsync(page);
            var detailDialog = page.Locator(".activity-day-detail-dialog");
            await detailDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await page.WaitForTimeoutAsync(1500);

            // Close day detail
            await detailDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
            await detailDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
            await page.WaitForTimeoutAsync(800);

            // 6. Navigate to Settings page
            await page.GotoAsync($"{BaseUrl}/settings?theme={suffix}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.WaitForTimeoutAsync(1500);

            // -- Tab 1: Profile and Preferences (Active by default)
            await SmoothScrollAsync(page, down: true);
            await page.WaitForTimeoutAsync(800);
            await SmoothScrollAsync(page, down: false);
            await page.WaitForTimeoutAsync(800);

            // -- Tab 2: Notifications
            await page.GetByRole(AriaRole.Tab, new() { Name = "Notifications" }).ClickAsync();
            await page.WaitForTimeoutAsync(800);
            await SmoothScrollAsync(page, down: true);
            await page.WaitForTimeoutAsync(800);
            await SmoothScrollAsync(page, down: false);
            await page.WaitForTimeoutAsync(800);

            // -- Tab 3: Security and Account
            await page.GetByRole(AriaRole.Tab, new() { Name = "Security & Account" }).ClickAsync();
            await page.WaitForTimeoutAsync(800);
            await SmoothScrollAsync(page, down: true);
            await page.WaitForTimeoutAsync(800);
            await SmoothScrollAsync(page, down: false);
            await page.WaitForTimeoutAsync(800);

            // End of walkthrough - close context to ensure video file is finalized
            await page.CloseAsync();
            await context.CloseAsync();

            // Copy recorded video to destination
            var video = page.Video;
            if (video != null)
            {
                var sourcePath = await video.PathAsync();
                var targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../docs/automation");
                var finalDestDir = Environment.GetEnvironmentVariable("E2E_VIDEO_OUT_DIR") ?? targetDir;
                Directory.CreateDirectory(finalDestDir);

                var finalPath = Path.Combine(finalDestDir, $"demo-video-{suffix}.webm");
                File.Copy(sourcePath, finalPath, overwrite: true);
                Console.WriteLine($"== Demo video saved to: {finalPath}");
            }
        }
    }

    private static async Task SmoothScrollAsync(IPage page, bool down)
    {
        await page.EvaluateAsync($$"""
            async () => {
                const getScrollContainer = () => {
                    const mainContent = document.querySelector('.mud-main-content');
                    if (mainContent && mainContent.scrollHeight > mainContent.clientHeight) {
                        return mainContent;
                    }
                    return window;
                };

                const container = getScrollContainer();
                const isWindow = container === window;
                
                const totalHeight = isWindow 
                    ? (document.documentElement.scrollHeight || document.body.scrollHeight)
                    : container.scrollHeight;
                const viewHeight = isWindow ? window.innerHeight : container.clientHeight;
                const scrollable = totalHeight - viewHeight;
                if (scrollable <= 0) return;

                const steps = 45;
                const delay = 16; // ~720ms total scroll duration
                const start = isWindow ? window.scrollY : container.scrollTop;
                const target = {{(down ? "scrollable" : "0")}};
                const diff = target - start;

                for (let i = 1; i <= steps; i++) {
                    const progress = i / steps;
                    const ease = progress < 0.5 
                        ? 2 * progress * progress 
                        : 1 - Math.pow(-2 * progress + 2, 2) / 2;
                    const nextVal = start + diff * ease;
                    if (isWindow) {
                        window.scrollTo(0, nextVal);
                    } else {
                        container.scrollTop = nextVal;
                    }
                    await new Promise(r => setTimeout(r, delay));
                }
            }
            """);
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
}
