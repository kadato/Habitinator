#pragma warning disable CA1724 // Type name 'App' conflicts with namespace 'App' - MAUI framework requires this naming convention

using App.MAUI.Services;
using App.MAUI.Services.LocalBoard;

using Microsoft.Extensions.Logging;

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
            _ = RunStartupTaskAsync(sp, "SQLite store init", store.EnsureStoreReadyAsync);
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
            _ = RunStartupTaskAsync(sp, "daily reminder reschedule", scheduler.SynchronizeAsync);
        }
    }

    private static async Task RunStartupTaskAsync(IServiceProvider services, string name, Func<CancellationToken, Task> task)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("App");
        try
        {
            await task(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Startup tasks are best-effort, board operations retry lazily.
            logger?.LogWarning(ex, "Startup task {TaskName} failed.", name);
        }
    }
}
