using System.Text.RegularExpressions;

using Microsoft.Playwright;

// Local dev default. Overridable via CLI argument or the E2E_BASE_URL environment variable.
#pragma warning disable S1075 // URIs are configuration defaults here, not hardcoded paths
const string DefaultBaseUrl = "http://localhost:5050";
#pragma warning restore S1075

if (args.Length > 0 && (args[0] == "-h" || args[0] == "--help"))
{
    await Console.Out.WriteLineAsync("Usage: Habitinator.Screenshots [baseUrl]");
    await Console.Out.WriteLineAsync($"  Default baseUrl: {DefaultBaseUrl} (or env E2E_BASE_URL)");
    await Console.Out.WriteLineAsync("  Output: docs/automation/screenshots/{name}-{light|dark}.png (or env E2E_SCREENSHOT_DIR)");
    return;
}

var baseUrl = (args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? DefaultBaseUrl).TrimEnd('/');
var repoRoot = FindRepoRoot();
var screenshotDir = Environment.GetEnvironmentVariable("E2E_SCREENSHOT_DIR")
    ?? Path.Combine(repoRoot, "docs", "automation", "screenshots");
Directory.CreateDirectory(screenshotDir);

WriteLine($"Screenshot output: {screenshotDir}");
WriteLine($"Target: {baseUrl}");

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

foreach (var (themeName, scheme) in new[] { ("light", ColorScheme.Light), ("dark", ColorScheme.Dark) })
{
    WriteLine($"--- CAPTURING {themeName.ToUpperInvariant()} THEME SCREENSHOTS ---");
    await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        ColorScheme = scheme
    });
    var page = await context.NewPageAsync();

    // Remove the WebAssembly loading splash so it never appears in stills
    await page.AddInitScriptAsync("""
        const removeSplash = () => {
            const splash = document.getElementById('habitinator-wasm-splash');
            if (splash) splash.remove();
        };
        removeSplash();
        const root = document.documentElement || document;
        new MutationObserver(removeSplash)
            .observe(root, { childList: true, subtree: true });
        """);

    // Auth screens first. No login needed.
    await CaptureAuthScreensAsync(page, baseUrl, themeName);

    // Log in as the demo guest
    await LoginAsGuestAsync(page, baseUrl);

    // Board + edit dialogs
    await CaptureBoardScreensAsync(page, themeName);

    // Running focus timer
    await CaptureTimerAsync(page, themeName);

    // Statistics + day detail dialog
    await CaptureStatisticsScreensAsync(page, baseUrl, themeName);

    // Settings
    await CaptureSettingsAsync(page, baseUrl, themeName);

    await context.CloseAsync();
}

WriteLine("All screenshots captured successfully!");
return;

async Task CaptureAuthScreensAsync(IPage page, string baseUrl, string theme)
{
    // Welcome / landing page
    await GotoAsync(page, baseUrl, "/");
    await page.GetByText("Welcome to Habitinator").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await Task.Delay(500);
    await CaptureAsync(page, "welcome", theme);

    // Login page
    await GotoAsync(page, baseUrl, "/auth/login");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" }).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await Task.Delay(500);
    await CaptureAsync(page, "login", theme);

    // Register page
    await GotoAsync(page, baseUrl, "/auth/register");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Create account" }).WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await Task.Delay(500);
    await CaptureAsync(page, "register", theme);
}

async Task LoginAsGuestAsync(IPage page, string baseUrl)
{
    await GotoAsync(page, baseUrl, "/");
    await page.GetByText("Welcome to Habitinator").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await Task.Delay(500);

    await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("demo guest", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(10)) }).ClickAsync(new() { Timeout = 30_000 });
    await page.WaitForURLAsync(u => u.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase) &&
                                    !u.Contains("/auth/login", StringComparison.OrdinalIgnoreCase),
        new() { Timeout = 60_000 });

    await DismissCatchUpDialogIfPresentAsync(page);
    await page.Locator(".board-shell").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    await Task.Delay(800);
}

async Task CaptureBoardScreensAsync(IPage page, string theme)
{
    // Board. Mobile tab layout, Habits tab is default.
    await page.Locator(".board-section-switcher").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await Task.Delay(500);
    await CaptureAsync(page, "board", theme);

    // Edit habit dialog. Habits tab is active by default.
    var habitDialog = await OpenEditDialogAsync(page, "habit");
    await CaptureAsync(page, "edit-habit", theme);
    await CloseDialogAsync(habitDialog);

    // Switch to Dailies tab and open the daily edit dialog
    await page.Locator(".board-section-switcher__btn", new() { HasText = "Dailies" }).ClickAsync();
    await Task.Delay(400);
    var dailyDialog = await OpenEditDialogAsync(page, "daily");
    await CaptureAsync(page, "edit-daily", theme);
    await CloseDialogAsync(dailyDialog);
}

async Task CaptureTimerAsync(IPage page, string theme)
{
    // The timer panel is collapsed on mobile. Expand it first.
    var mobileHeader = page.Locator(".timer-mobile-header");
    if (await mobileHeader.CountAsync() > 0)
    {
        await mobileHeader.ClickAsync();
        await Task.Delay(500);
    }

    // Set a session target from the first habit title
    var habitTitle = (await page.Locator(".board-column--habit .board-card__title-text").First.TextContentAsync())?.Trim()
        ?? "Deep work";
    var targetInput = page.Locator(".timer-target-field input");
    await targetInput.ClickAsync();
    await targetInput.FillAsync(habitTitle);
    await targetInput.PressAsync("Enter");
    await Task.Delay(500);

    // Start the stopwatch and let it tick for a couple of seconds
    var startBtn = page.Locator(".timer-icon-btn--start:not([disabled])");
    await startBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    await startBtn.ClickAsync();
    await Task.Delay(3000);

    await CaptureAsync(page, "timer", theme);

    // Pause, then reset so the next theme starts clean
    var pauseBtn = page.Locator(".timer-icon-btn--pause:not([disabled])");
    if (await pauseBtn.CountAsync() > 0)
    {
        await pauseBtn.ClickAsync();
        await Task.Delay(500);
    }
    var resetBtn = page.Locator("button[aria-label=\"Reset timer\"]:not([disabled])");
    if (await resetBtn.CountAsync() > 0)
    {
        await resetBtn.ClickAsync();
        await Task.Delay(300);
    }
}

async Task CaptureStatisticsScreensAsync(IPage page, string baseUrl, string theme)
{
    await GotoAsync(page, baseUrl, "/stats");
    await page.Locator(".stats-heatmap-day-btn").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await Task.Delay(800);
    await CaptureAsync(page, "statistics", theme);

    await ClickDiverseHeatmapDayAsync(page);
    var detailDialog = page.Locator(".activity-day-detail-dialog");
    await detailDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await Task.Delay(1000);
    await CaptureAsync(page, "activity-day-detail", theme);
    await detailDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
    await detailDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
}

async Task CaptureSettingsAsync(IPage page, string baseUrl, string theme)
{
    await GotoAsync(page, baseUrl, "/settings");
    await page.Locator(".settings-page-wrapper").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await Task.Delay(800);
    await CaptureAsync(page, "settings", theme);
}

async Task<ILocator> OpenEditDialogAsync(IPage page, string section)
{
    var cardTitle = page.Locator($".board-column--{section} .board-card__title").First;
    await cardTitle.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await cardTitle.ClickAsync();
    var dialog = page.Locator(".edit-daily-dialog");
    await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await Task.Delay(500);
    return dialog;
}

static async Task CloseDialogAsync(ILocator dialog)
{
    await dialog.Locator(".edit-daily-header__close").ClickAsync();
    await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
}

static async Task GotoAsync(IPage page, string baseUrl, string relativeUrl)
{
    WriteLine($"Navigating to: {relativeUrl}");
    await page.GotoAsync($"{baseUrl}{relativeUrl}", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
}

async Task CaptureAsync(IPage page, string name, string theme)
{
    var filepath = Path.Combine(screenshotDir, $"{name}-{theme}.png");

    // Hide scrollbars so the still looks clean
    await page.EvaluateAsync("""
        () => {
            const style = document.createElement('style');
            style.id = 'hab-screenshot-hide-scrollbars';
            style.textContent = `
                html, body { overflow: hidden !important; }
                * { scrollbar-width: none !important; }
                *::-webkit-scrollbar { display: none !important; }
            `;
            document.head.appendChild(style);
        }
        """);
    try
    {
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = filepath });
    }
    finally
    {
        await page.EvaluateAsync("document.getElementById('hab-screenshot-hide-scrollbars')?.remove()");
    }
    WriteLine($"Captured: {filepath}");
}

static async Task DismissCatchUpDialogIfPresentAsync(IPage page)
{
    var dialog = page.Locator(".daily-yesterday-dialog");
    try
    {
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });
    }
    catch (TimeoutException)
    {
        return;
    }

    await dialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync(new() { Timeout = 10_000 });
    await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
}

static async Task ClickDiverseHeatmapDayAsync(IPage page)
{
    var clicked = await page.EvaluateAsync<bool>(
        """
        async () => {
            const dashRes = await fetch('/api/activity/dashboard?period=r370');
            const dash = await dashRes.json();
            // Buttons render for every in-data-range cell in row-major order,
            // including days with zero events.
            const ordered = (dash.heatmap || [])
                .filter(c => c.inDataRange)
                .sort((a, b) => a.dayRow - b.dayRow || a.weekCol - b.weekCol);
            const active = ordered.filter(c => c.count > 0);

            // Score every active day, not just the busiest ones, so a day with
            // many distinct event types wins even if its total is lower.
            const scored = await Promise.all(active.map(async (c) => {
                const detailRes = await fetch('/api/activity/day?date=' + c.date);
                const detail = await detailRes.json();
                const events = detail.events || [];
                return {
                    date: c.date,
                    types: new Set(events.map(e => e.eventType)).size,
                    count: events.length
                };
            }));
            scored.sort((a, b) => b.types - a.types || b.count - a.count);
            const best = scored[0];
            if (!best) return false;

            const idx = ordered.findIndex(c => c.date === best.date);
            const buttons = document.querySelectorAll('button.stats-heatmap-day-btn');
            if (idx >= 0 && idx < buttons.length) {
                buttons[idx].click();
                return true;
            }
            return false;
        }
        """);

    if (clicked)
    {
        return;
    }

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

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Habitinator.slnx")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();
}

static void WriteLine(string message) => Console.WriteLine(message);
