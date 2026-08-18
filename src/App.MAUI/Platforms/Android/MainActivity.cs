using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;

using AndroidX.Core.View;

namespace App.MAUI;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        EnableEdgeToEdge(Window, Resources);
    }

    private static void EnableEdgeToEdge(Android.Views.Window? window, Resources? resources)
    {
        if (window == null)
        {
            return;
        }

        // Allow content to draw behind system bars, the status bar and navigation bar.
        WindowCompat.SetDecorFitsSystemWindows(window, false);

        // Make status bar and navigation bar transparent so the app background shows through
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            window.SetStatusBarColor(Android.Graphics.Color.Transparent);
            window.SetNavigationBarColor(Android.Graphics.Color.Transparent);
        }

        UpdateStatusBarAppearance(window, resources);
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);

        // When following system theme, UserAppTheme == Unspecified, react to
        // system dark mode changes so status bar icons stay readable
        if (Microsoft.Maui.Controls.Application.Current?.UserAppTheme ==
            Microsoft.Maui.ApplicationModel.AppTheme.Unspecified)
        {
            UpdateStatusBarAppearance(Window, Resources);
        }
    }

    private static void UpdateStatusBarAppearance(Android.Views.Window? window, Resources? resources)
    {
        if (window?.DecorView == null)
        {
            return;
        }

        var insetsController = WindowCompat.GetInsetsController(window, window.DecorView);
        if (insetsController == null)
        {
            return;
        }

        var isDarkMode = ResolveIsDarkMode(resources);

        // On dark backgrounds we need light, white status bar icons. AppearanceLightStatusBars = false
        // On light backgrounds we need dark status bar icons. AppearanceLightStatusBars = true
        insetsController.AppearanceLightStatusBars = !isDarkMode;
        insetsController.AppearanceLightNavigationBars = !isDarkMode;
    }

    private static bool ResolveIsDarkMode(Resources? resources)
    {
        // Respect the app's explicit theme choice when set
        var userAppTheme = Microsoft.Maui.Controls.Application.Current?.UserAppTheme;
        if (userAppTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark)
        {
            return true;
        }

        if (userAppTheme == Microsoft.Maui.ApplicationModel.AppTheme.Light)
        {
            return false;
        }

        // Fall back to the system dark mode setting
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            return resources?.Configuration?.IsNightModeActive ?? false;
        }

        var uiMode = resources?.Configuration?.UiMode;
        return uiMode.HasValue && ((int)uiMode.Value & (int)UiMode.NightMask) == (int)UiMode.NightYes;
    }
}
