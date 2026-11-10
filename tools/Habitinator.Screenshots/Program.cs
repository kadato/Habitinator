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

    // Sign in as the demo guest
    await LoginAsGuestAsync(page, baseUrl);

    // Board + edit dialogs
    await CaptureBoardScreensAsync(page, baseUrl, themeName);

    // AI assistant drawer
    await CaptureAiAssistantAsync(page, baseUrl, themeName);

    // Running focus timer
    await CaptureTimerAsync(page, themeName);

    // Statistics + day detail dialog
    await CaptureStatisticsScreensAsync(page, baseUrl, themeName);

    // Upcoming calendar
    await CaptureUpcomingAsync(page, baseUrl, themeName);

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

async Task CaptureBoardScreensAsync(IPage page, string baseUrl, string theme)
{
    // Board. Mobile tab layout, Habits tab is default.
    await GotoAsync(page, baseUrl, "/");
    await page.Locator(".board-shell").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    await DismissCatchUpDialogIfPresentAsync(page);
    await page.Locator(".board-section-switcher").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    await Task.Delay(500);
    await CaptureAsync(page, "board", theme);

    // The yesterday retro dialog can show a moment after the board loads. Clear it before
    // opening the edit dialogs so it does not swallow the card click.
    await DismissCatchUpDialogIfPresentAsync(page);

    // Edit habit dialog. Habits tab is active by default.
    var habitDialog = await OpenEditDialogAsync(page, "habit");
    await CaptureAsync(page, "edit-habit", theme);
    await CloseDialogAsync(habitDialog);

    // Switch to Dailies tab and open the daily edit dialog
    await page.Locator(".board-section-switcher__btn", new() { HasText = "Dailies" }).ClickAsync();
    await Task.Delay(400);
    await DismissCatchUpDialogIfPresentAsync(page);
    var dailyDialog = await OpenEditDialogAsync(page, "daily");
    await CaptureAsync(page, "edit-daily", theme);
    await CloseDialogAsync(dailyDialog);
}

async Task CaptureAiAssistantAsync(IPage page, string baseUrl, string theme)
{
    // The drawer opens from the board. Make sure we are back on the board root.
    await GotoAsync(page, baseUrl, "/");
    await page.Locator(".board-shell").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    await DismissCatchUpDialogIfPresentAsync(page);
    await Task.Delay(400);

    // Send a real message through the drawer and wait for the live answer.
    // The demo guest has no API key saved, so the server falls back to the
    // repo .env OPENCODE_API_KEY for these screenshot runs.
    var aiButton = page.GetByRole(AriaRole.Button, new() { Name = "AI assistant" });
    // The board reloads its data after the Goto above, so the AI button wait uses the same 60-second budget as the board shell wait.
    await aiButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    await aiButton.ClickAsync();
    await page.Locator("#ai-messages-scroll-area").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    await Task.Delay(500);

    var prompt = page.GetByPlaceholder("Ask AI...");
    await prompt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    await prompt.FillAsync("What should I focus on today?");
    await page.GetByRole(AriaRole.Button, new() { Name = "Send message" }).ClickAsync();

    // The user bubble proves the send started. The button returns to
    // "Send message" once streaming finishes.
    await page.Locator(".ai-message--user", new() { HasText = "What should I focus on today?" })
        .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await page.GetByRole(AriaRole.Button, new() { Name = "Send message" })
        .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 180_000 });

    var lastAnswer = page.Locator(".ai-message--assistant").Last.Locator(".ai-rendered-markdown");
    await lastAnswer.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    var answerText = (await lastAnswer.InnerTextAsync())?.Trim() ?? string.Empty;
    if (answerText.Length < 20 || answerText.StartsWith("⚠️", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The AI answer did not arrive. Refusing to screenshot an empty chat.");
    }

    // Scroll the user bubble to the top of the scroll area. The capture then shows the question and the start of the answer.
    await page.EvaluateAsync("document.querySelector('#ai-messages-scroll-area .ai-message--user')?.scrollIntoView({ block: 'start' })");
    await Task.Delay(600);
    await CaptureAsync(page, "ai-assistant", theme);

    var closeButton = page.GetByRole(AriaRole.Button, new() { Name = "Close assistant" });
    if (await closeButton.CountAsync() > 0)
    {
        await closeButton.First.ClickAsync();
        await Task.Delay(400);
    }
    else
    {
        await page.Keyboard.PressAsync("Escape");
        await Task.Delay(400);
    }
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
    var startBtn = page.Locator("button[aria-label=\"Start timer\"]:not([disabled]), button[aria-label=\"Resume timer\"]:not([disabled])");
    await startBtn.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    await startBtn.First.ClickAsync();
    await Task.Delay(3000);

    await CaptureAsync(page, "timer", theme);

    // Pause, then reset so the next theme starts clean
    var pauseBtn = page.Locator("button[aria-label=\"Pause timer\"]:not([disabled])");
    if (await pauseBtn.CountAsync() > 0)
    {
        await pauseBtn.First.ClickAsync();
        await Task.Delay(500);
    }
    var resetBtn = page.Locator("button[aria-label=\"Reset timer\"]:not([disabled])");
    if (await resetBtn.CountAsync() > 0)
    {
        await resetBtn.First.ClickAsync();
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
    var detailDialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Day details" });
    await detailDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    await Task.Delay(1000);
    await CaptureAsync(page, "activity-day-detail", theme);
    await detailDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
    await detailDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
}

async Task CaptureUpcomingAsync(IPage page, string baseUrl, string theme)
{
    await GotoAsync(page, baseUrl, "/upcoming");
    await page.Locator(".upcoming-panel").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    // .cal-body only renders after data loads. The skeleton also uses .cal-top, so it is not a load marker.
    // The server prerenders one state, and the WASM runtime briefly swaps back to the
    // skeleton on takeover. Wait until the loaded DOM stays put, then capture.
    const string loadedExpr = "() => document.querySelectorAll('.cal-body').length > 0 && document.querySelectorAll('.cal-skel-grid').length === 0";
    for (var attempt = 0; attempt < 3; attempt++)
    {
        await page.WaitForFunctionAsync(loadedExpr, null, new() { Timeout = 30_000 });
        await Task.Delay(2000);
        if (await page.EvaluateAsync<bool>(loadedExpr))
        {
            break;
        }
    }
    await Task.Delay(800);
    await CaptureAsync(page, "upcoming", theme);
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
    var close = dialog.Locator(".hab-modal__close, .edit-daily-header__close");
    await close.First.ClickAsync();
    await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
}

static async Task GotoAsync(IPage page, string baseUrl, string relativeUrl)
{
    WriteLine($"Navigating to: {relativeUrl}");
    // The board holds a SignalR socket open, so NetworkIdle never settles.
    // The Load event fires first, and the selector waits after every navigation prove the page loaded.
    await page.GotoAsync($"{baseUrl}{relativeUrl}", new PageGotoOptions { WaitUntil = WaitUntilState.Load });
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
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 6000 });
    }
    catch (TimeoutException)
    {
        return;
    }

    await dialog.Locator(".hab-modal__close, .daily-yesterday-header__close-btn").First.ClickAsync(new() { Timeout = 10_000 });
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
