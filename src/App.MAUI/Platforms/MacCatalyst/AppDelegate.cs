#pragma warning disable CA1711 // MacCatalyst AppDelegate names should end in 'Delegate'

using Foundation;

namespace App.MAUI;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
