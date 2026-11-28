using Deque.AxeCore.Playwright;

using FluentAssertions;

using Microsoft.Playwright;

namespace App.Web.E2E;

/// <summary>Checks that the tag manager opens from the board and is accessible.</summary>
public sealed class TagManagerE2ETests
{
    [SkippableFact]
    public async Task Guest_opens_the_tag_manager_from_the_tags_menu()
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

        await page.Locator("button.board-trigger-btn").Filter(new LocatorFilterOptions { HasText = "Tags" })
            .ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Manage tags" })
            .ClickAsync(new LocatorClickOptions { Timeout = 10_000 });

        var dialog = page.Locator(".tag-manager");
        await Assertions.Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(dialog.Locator(".tag-manager__item").Filter(new LocatorFilterOptions { HasText = "work" }))
            .ToContainTextAsync("items");

        await dialog.Locator("button[aria-label='Rename tag travel']").ClickAsync();
        await dialog.Locator(".tag-manager__input").FillAsync("trips");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Assertions.Expect(dialog.Locator(".tag-manager__status"))
            .ToContainTextAsync("Renamed \"travel\" to \"trips\"", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Assertions.Expect(dialog.Locator(".tag-manager__item").Filter(new LocatorFilterOptions { HasText = "trips" }))
            .ToBeVisibleAsync();

        await dialog.Locator("button[aria-label='Delete tag trips']").ClickAsync();
        await Assertions.Expect(dialog).ToContainTextAsync("Remove \"trips\"");

        var results = await dialog.RunAxe();
        var failures = results.Violations
            .Where(v => v.Impact is "serious" or "critical")
            .Select(v => $"{v.Id} ({v.Impact}): {v.Help} at {string.Join(" | ", v.Nodes.Select(n => string.Join(" ", n.Target)))}")
            .ToList();
        failures.Should().BeEmpty("the tag manager must not have serious or critical axe violations");
    }
}
