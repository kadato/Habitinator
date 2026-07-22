using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using AndroidX.Core.View;

namespace App.MAUI;

[Activity(Theme = "@style/Maui.MainTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        EnableEdgeToEdge();
    }

    private void EnableEdgeToEdge()
    {
        if (Window == null) return;

        // Allow content to draw behind system bars (status bar + navigation bar)
        WindowCompat.SetDecorFitsSystemWindows(Window, false);

        // Make status bar and navigation bar transparent so the app background shows through
        Window.SetStatusBarColor(Android.Graphics.Color.Transparent);
        Window.SetNavigationBarColor(Android.Graphics.Color.Transparent);

        UpdateStatusBarAppearance();
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);

        // When following system theme (UserAppTheme == Unspecified), react to
        // system dark mode changes so status bar icons stay readable
        if (Microsoft.Maui.Controls.Application.Current?.UserAppTheme ==
            Microsoft.Maui.ApplicationModel.AppTheme.Unspecified)
        {
            UpdateStatusBarAppearance();
        }
    }

    private void UpdateStatusBarAppearance()
    {
        if (Window?.DecorView == null) return;

        var insetsController = WindowCompat.GetInsetsController(Window, Window.DecorView);
        if (insetsController == null) return;

        var isDarkMode = ResolveIsDarkMode();

        // On dark backgrounds we need light (white) status bar icons → AppearanceLightStatusBars = false
        // On light backgrounds we need dark status bar icons       → AppearanceLightStatusBars = true
        insetsController.AppearanceLightStatusBars = !isDarkMode;
        insetsController.AppearanceLightNavigationBars = !isDarkMode;
    }

    private bool ResolveIsDarkMode()
    {
        // Respect the app's explicit theme choice when set
        var userAppTheme = Microsoft.Maui.Controls.Application.Current?.UserAppTheme;
        if (userAppTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark) return true;
        if (userAppTheme == Microsoft.Maui.ApplicationModel.AppTheme.Light) return false;

        // Fall back to the system dark mode setting
        return (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
    }
}
