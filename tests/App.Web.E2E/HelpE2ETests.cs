using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

using FluentAssertions;

using Microsoft.Playwright;

namespace App.Web.E2E;

/// <summary>Guest-board smoke coverage for the help dialog and accessibility.</summary>
public sealed class HelpE2ETests
{
    private static readonly AxeRunOptions BoardRunOptions = new()
    {
        Rules = new Dictionary<string, RuleOptions>
        {
            // MudMenu wraps its activator in a div[role=button], so every menu
            // trips this rule even when the inner button is the real target.
            ["nested-interactive"] = new() { Enabled = false }
        }
    };

    [SkippableFact]
    public async Task Guest_board_opens_help_and_both_pass_axe()
    {
        await WebE2E.EnsureBaseUrlReachableAsync();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(
            $"{WebE2E.BaseUrl}/",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await page.GetByRole(AriaRole.Button, new() { Name = "Try the demo board" })
            .ClickAsync(new LocatorClickOptions { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".board-shell", new PageWaitForSelectorOptions { Timeout = 30_000 });
        await page.WaitForFunctionAsync(
            "() => !!window.HabitinatorKeyboardShortcuts",
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        await page.Locator(".board-today-strip").ClickAsync();

        var boardResults = await page.RunAxe(BoardRunOptions);
        var boardFailures = DescribeFailures(boardResults, includeSerious: true);
        boardFailures.Should().BeEmpty("the guest board must not have serious or critical axe violations");

        await page.Keyboard.PressAsync("?");
        await Assertions.Expect(page.Locator(".help-dialog"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Assertions.Expect(page.Locator(".help-dialog"))
            .ToContainTextAsync("Keyboard shortcuts and quick add");

        var helpResults = await page.Locator(".help-dialog").RunAxe();
        var helpFailures = DescribeFailures(helpResults, includeSerious: true);
        helpFailures.Should().BeEmpty("the help dialog must not have serious or critical axe violations");
    }

    private static List<string> DescribeFailures(AxeResult results, bool includeSerious) =>
        results.Violations
            .Where(v => v.Impact == "critical" || (includeSerious && v.Impact == "serious"))
            .Select(v => $"{v.Id} ({v.Impact}): {v.Help} at {string.Join(" | ", v.Nodes.Select(n => $"{string.Join(" ", n.Target)} => {n.Html}"))}")
            .ToList();
}
