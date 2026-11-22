using App.Shared.RCL.Services.Board.Local;
using App.Shared.RCL.Services.CommandPalette;
using App.Shared.RCL.Services.Remote;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MudBlazor;
using MudBlazor.Services;

namespace App.Shared.RCL.Services;

/// <summary>
///     Registers the services shared by the WebAssembly and MAUI clients. Each host
///     registers only its storage, session, and platform adapters, so the two clients
///     cannot drift apart one service at a time.
/// </summary>
public static class ClientServiceRegistration
{
    /// <summary>Registers MudBlazor with the snackbar behavior both clients share.</summary>
    public static IServiceCollection AddHabitinatorMudServices(this IServiceCollection services)
    {
        services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomLeft;
            config.SnackbarConfiguration.ShowTransitionDuration = 100;
            config.SnackbarConfiguration.HideTransitionDuration = 100;
            config.SnackbarConfiguration.NewestOnTop = true;
        });
        return services;
    }

    /// <param name="services">The host service collection.</param>
    /// <param name="stateLifetime">
    ///     Lifetime for app-wide stateful services. WebAssembly uses scoped, one scope per app.
    ///     MAUI uses singleton because platform services hold the board service for the app lifetime.
    /// </param>
    /// <param name="isOfflineProbe">Host connectivity probe used by the board sync coordinator.</param>
    public static IServiceCollection AddHabitinatorClientCore(
        this IServiceCollection services,
        ServiceLifetime stateLifetime,
        Func<IServiceProvider, bool>? isOfflineProbe = null)
    {
        AddStatelessServices(services);
        AddStateServices(services, stateLifetime, isOfflineProbe);
        AddComponentServices(services);
        return services;
    }

    private static void AddStatelessServices(IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IActivityEventStore, SettingsActivityEventStore>();
        services.AddSingleton<OfflineActivityStatisticsProvider>();
    }

    private static void AddStateServices(
        IServiceCollection services,
        ServiceLifetime lifetime,
        Func<IServiceProvider, bool>? isOfflineProbe)
    {
        Add(lifetime, typeof(IUserDayService), typeof(UserDayService));
        Add(lifetime, typeof(GlobalTimerService), typeof(GlobalTimerService));
        Add(lifetime, typeof(RemoteBoardRefreshService), typeof(RemoteBoardRefreshService));
        Add(lifetime, typeof(IRemoteBoardRefreshService), typeof(SyncBeforeNotifyRefreshService));
        Add(lifetime, typeof(BoardRemoteNotifyBridge), typeof(BoardRemoteNotifyBridge));
        Add(lifetime, typeof(RemoteBoardDataService), typeof(RemoteBoardDataService));
        Add(lifetime, typeof(BoardSyncStatus), typeof(BoardSyncStatus));
        Add(lifetime, typeof(BoardSyncPausePreference), typeof(BoardSyncPausePreference));
        Add(lifetime, typeof(BoardInitialLoadSignal), typeof(BoardInitialLoadSignal));
        Add(lifetime, typeof(LocalFirstBoardDataService), typeof(LocalFirstBoardDataService));
        Add(lifetime, typeof(IUserTimeZoneService), typeof(UserTimeZoneService));
        Add(lifetime, typeof(IUserPreferencesService), typeof(LocalFirstUserPreferencesService));
        Add(lifetime, typeof(IUserDateFormatService), typeof(UserDateFormatService));
        Add(lifetime, typeof(INotificationSettingsService), typeof(RemoteNotificationSettingsService));
        Add(lifetime, typeof(INotificationSettingsRules), typeof(NotificationSettingsRules));
        Add(lifetime, typeof(IUserActivityLogService), typeof(RemoteUserActivityLogService));
        Add(lifetime, typeof(RemoteActivityStatisticsReader), typeof(RemoteActivityStatisticsReader));
        Add(lifetime, typeof(IActivityStatisticsReader), typeof(PauseAwareActivityStatisticsReader));
        Add(lifetime, typeof(IAccountActionsService), typeof(RemoteAccountActionsService));
        Add(lifetime, typeof(IUserDataExportService), typeof(RemoteUserDataExportService));
        Add(lifetime, typeof(IUserDataImportService), typeof(RemoteUserDataImportService));
        Add(lifetime, typeof(IAiAssistantService), typeof(RemoteAiAssistantService));
        Add(lifetime, typeof(MobileBackHandler), typeof(MobileBackHandler));

        services.Add(new ServiceDescriptor(
            typeof(IBoardSyncStatus),
            sp => sp.GetRequiredService<BoardSyncStatus>(),
            lifetime));
        services.Add(new ServiceDescriptor(
            typeof(IBoardLocalStoreLifecycle),
            sp => sp.GetRequiredService<LocalFirstBoardDataService>(),
            lifetime));
        services.Add(new ServiceDescriptor(
            typeof(IInitialBoardLoadGate),
            sp => new InitialBoardLoadGate(sp.GetRequiredService<BoardInitialLoadSignal>()),
            lifetime));
        services.Add(new ServiceDescriptor(
            typeof(BoardSyncCoordinator),
            sp => new BoardSyncCoordinator(
                sp.GetRequiredService<LocalFirstBoardDataService>(),
                sp.GetRequiredService<ICurrentUserKeyProvider>(),
                sp.GetRequiredService<BoardSyncStatus>(),
                sp.GetRequiredService<BoardSyncPausePreference>(),
                sp.GetRequiredService<BoardInitialLoadSignal>(),
                sp.GetRequiredService<RemoteBoardRefreshService>(),
                sp.GetRequiredService<ILogger<BoardSyncCoordinator>>(),
                sp,
                isOfflineProbe is null ? null : () => isOfflineProbe(sp)),
            lifetime));
        services.Add(new ServiceDescriptor(
            typeof(IBoardSyncRequestor),
            sp => sp.GetRequiredService<BoardSyncCoordinator>(),
            lifetime));

        void Add(ServiceLifetime serviceLifetime, Type serviceType, Type implementationType) =>
            services.Add(new ServiceDescriptor(serviceType, implementationType, serviceLifetime));
    }

    private static void AddComponentServices(IServiceCollection services)
    {
        services.AddScoped<IUndoService, UndoService>();
        services.AddScoped<ITimerSessionLogService, TimerSessionLogService>();
        services.AddScoped<IUserNotifier, UserNotifier>();
        services.AddScoped<IFocusTimerClientAlerts, FocusTimerClientAlerts>();
        services.AddScoped<IDailyRetroPromptStore, JsDailyRetroPromptStore>();
        services.AddScoped<IBoardColumnStateStore, JsBoardColumnStateStore>();
        services.AddScoped<IUpcomingViewStateStore, UpcomingViewStateStore>();
        services.AddScoped<BoardUiSessionState>();
        services.AddScoped<ICommandPaletteService, CommandPaletteService>();
        services.AddScoped<IBoardDataService>(sp =>
        {
            var inner = sp.GetRequiredService<LocalFirstBoardDataService>();
            var undoService = sp.GetRequiredService<IUndoService>();
            return new UndoableBoardDataService(inner, undoService);
        });
    }
}
