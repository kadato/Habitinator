using App.MAUI.Services;
using App.MAUI.Services.LocalBoard;

namespace App.MAUI;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage()) { Title = "Habitinator" };
    }

    protected override void OnStart()
    {
        base.OnStart();
        RequestLocalStoreReady();
        RequestDailyReminderReschedule();
    }

    private static void RequestLocalStoreReady()
    {
        if (IPlatformApplication.Current?.Services is not IServiceProvider sp)
        {
            return;
        }

        if (sp.GetService(typeof(IMauiBoardLocalStoreLifecycle)) is IMauiBoardLocalStoreLifecycle store)
        {
            _ = store.EnsureStoreReadyAsync();
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        RequestDailyReminderReschedule();
        RequestBoardSync();
    }

    private static void RequestBoardSync()
    {
        if (IPlatformApplication.Current?.Services is not IServiceProvider sp)
        {
            return;
        }

        if (sp.GetService(typeof(MauiBoardSyncCoordinator)) is MauiBoardSyncCoordinator sync)
        {
            sync.RequestSync();
        }
    }

    private static void RequestDailyReminderReschedule()
    {
        if (IPlatformApplication.Current?.Services is not IServiceProvider sp)
        {
            return;
        }

        if (sp.GetService(typeof(MauiDailyReminderService)) is MauiDailyReminderService scheduler)
        {
            _ = scheduler.SynchronizeAsync();
        }
    }
}
