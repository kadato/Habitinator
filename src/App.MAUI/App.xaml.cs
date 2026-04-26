using App.MAUI.Services;

namespace App.MAUI;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage()) { Title = "App.MAUI" };
    }

    protected override void OnStart()
    {
        base.OnStart();
        RequestDailyReminderReschedule();
    }

    protected override void OnResume()
    {
        base.OnResume();
        RequestDailyReminderReschedule();
    }

    private static void RequestDailyReminderReschedule()
    {
        if (IPlatformApplication.Current?.Services is not IServiceProvider sp) return;

        if (sp.GetService(typeof(MauiDailyReminderService)) is MauiDailyReminderService scheduler)
            _ = scheduler.SynchronizeAsync();
    }
}
