using FluentAssertions;

using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Support.UI;

using Xunit;

namespace App.MAUI.UITests;

/// <summary>Native-shell checks for the MAUI Blazor app on Android (Appium + UiAutomator2).</summary>
public sealed class AndroidSmokeTests
{
    private static AndroidDriver CreateDriver(string apkPath)
    {
        var uri = new Uri(AndroidUiTestEnvironment.AppiumServerUrl);
        var options = new AppiumOptions();
        options.PlatformName = "Android";
        options.AutomationName = "UIAutomator2";
        options.App = apkPath;

        return new AndroidDriver(uri, options, TimeSpan.FromMinutes(3));
    }

    [SkippableFact]
    public void App_launches_and_BlazorWebView_is_accessible()
    {
        Skip.If(!AndroidUiTestEnvironment.TryGetApkPath(out var apk), AndroidUiTestEnvironment.SkipReason ?? "APK missing.");

        using var driver = CreateDriver(apk);
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(45));
        var webView = wait.Until(d => d.FindElement(MobileBy.AccessibilityId("uitest-blazor-webview")));
        webView.Should().NotBeNull();
    }

    [SkippableFact]
    public void Main_page_automation_id_present()
    {
        Skip.If(!AndroidUiTestEnvironment.TryGetApkPath(out var apk), AndroidUiTestEnvironment.SkipReason ?? "APK missing.");

        using var driver = CreateDriver(apk);
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
        var page = wait.Until(d => d.FindElement(MobileBy.AccessibilityId("uitest-main-page")));
        page.Should().NotBeNull();
    }
}
