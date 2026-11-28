using Deque.AxeCore.Playwright;

using FluentAssertions;

using Microsoft.Playwright;

namespace App.Web.E2E;

/// <summary>Runs axe-core in Chromium. The target app must be running. See <see cref="WebE2E" />.</summary>
public sealed class AccessibilityTests
{
    [SkippableTheory]
    [InlineData("/")]
    [InlineData("/auth/login")]
    [InlineData("/auth/register")]
    public async Task Page_has_no_serious_or_critical_axe_violations(string path)
    {
        await WebE2E.EnsureBaseUrlReachableAsync();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(
            $"{WebE2E.BaseUrl}{path}",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var results = await page.RunAxe();
        var failures = results.Violations
            .Where(v => v.Impact is "serious" or "critical")
            .Select(v => $"{v.Id} ({v.Impact}): {v.Help}")
            .ToList();

        failures.Should().BeEmpty("axe found serious or critical violations on {0}", path);
    }
}
