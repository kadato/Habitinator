using App.MAUI.Data;
using App.MAUI.Services;
using App.MAUI.Services.LocalBoard;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Remote;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

using MudBlazor;
using MudBlazor.Services;

using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models.AndroidOption;

namespace App.MAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        AddEmbeddedAppSettings(builder);
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("PlusJakartaSans-Regular.ttf", "PlusJakartaSans");
                fonts.AddFont("PlusJakartaSans-Medium.ttf", "PlusJakartaSansMedium");
                fonts.AddFont("PlusJakartaSans-Bold.ttf", "PlusJakartaSansBold");
            });
        builder.UseLocalNotification(config =>
        {
            _ = config.AddAndroid(a => a.AddChannel(new AndroidNotificationChannelRequest
            {
                Id = MauiDailyReminderService.AndroidChannelId,
                Name = "Daily reminder",
                Description = "Dailies due and to-dos with deadlines"
            }));
        });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomLeft;
            config.SnackbarConfiguration.ShowTransitionDuration = 250;
            config.SnackbarConfiguration.HideTransitionDuration = 200;
            config.SnackbarConfiguration.NewestOnTop = true;
        });
        string apiBase = MauiAppSettings.ResolveApiBaseUrl(builder.Configuration).TrimEnd('/') + "/";
        builder.Services.AddSingleton(_ => new MauiApiEndpointOptions(apiBase));
        builder.Services.AddSingleton<IAuthTokenStore, AuthTokenStore>();
        builder.Services.AddSingleton<ILocalSettingsStore, MauiLocalSettingsStore>();
        builder.Services.AddSingleton<IClientSessionProvider, MauiClientSessionProvider>();
        string localBoardDbPath = Path.Combine(FileSystem.AppDataDirectory, "habitinator-board-local.db");
        builder.Services.AddDbContextFactory<LocalBoardDbContext>(o =>
            o.UseSqlite($"Data Source={localBoardDbPath}"));
        builder.Services.AddSingleton<RemoteBoardRefreshService>();
        builder.Services.AddSingleton<MauiBoardSyncStatus>();
        builder.Services.AddSingleton<IBoardSyncStatus>(sp => sp.GetRequiredService<MauiBoardSyncStatus>());
        builder.Services.AddSingleton<ConflictResolutionService>();
        builder.Services.AddSingleton<MauiInitialBoardLoadSignal>();
        builder.Services.AddSingleton<MauiBoardSyncCoordinator>();
        builder.Services.AddSingleton<IRemoteBoardRefreshService>(sp =>
            new PullBeforeNotifyRemoteBoardRefreshService(
                sp.GetRequiredService<RemoteBoardRefreshService>(),
                sp.GetRequiredService<MauiBoardSyncCoordinator>()));
        builder.Services.AddSingleton<BoardRemoteNotifyBridge>();
        builder.Services.AddSingleton<MauiBoardHubService>();
        builder.Services.AddTransient<AuthMessageHandler>();
        builder.Services.AddTransient<ClearSessionOnUnauthorizedHandler>();
        builder.Services.AddHttpClient("apiAuth", c => c.BaseAddress = new Uri(apiBase));
        builder.Services.AddHttpClient("api", c => c.BaseAddress = new Uri(apiBase))
            .AddHttpMessageHandler<AuthMessageHandler>()
            .AddHttpMessageHandler<ClearSessionOnUnauthorizedHandler>()
            .AddStandardResilienceHandler(static options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
            });
        builder.Services.AddSingleton<ApiAuthService>();
        builder.Services.AddSingleton<RemoteBoardDataService>();
        builder.Services.AddSingleton<LocalFirstBoardDataService>();
        builder.Services.AddSingleton<IMauiBoardLocalStoreLifecycle>(sp =>
            sp.GetRequiredService<LocalFirstBoardDataService>());
        builder.Services.AddSingleton<IApiSession, ApiSession>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<GlobalTimerService>();
#if WINDOWS
        builder.Services.AddSingleton<IAppWindowProgressService, global::App.MAUI.Platforms.Windows.WindowsAppWindowProgressService>();
#else
        builder.Services.AddSingleton<IAppWindowProgressService, Services.MauiAppWindowProgressService>();
#endif
        builder.Services.AddScoped<ITimerSessionLogService, TimerSessionLogService>();
        builder.Services.AddSingleton<IUserActivityLogService, RemoteUserActivityLogService>();
        builder.Services.AddScoped<IUndoService, UndoService>();
        builder.Services.AddScoped<IBoardDataService>(sp =>
        {
            LocalFirstBoardDataService inner = sp.GetRequiredService<LocalFirstBoardDataService>();
            IUserActivityLogService log = sp.GetRequiredService<IUserActivityLogService>();
            ActivityLoggingBoardDataService loggingService = new(inner, log);
            IUndoService undoService = sp.GetRequiredService<IUndoService>();
            return new UndoableBoardDataService(loggingService, undoService);
        });
        builder.Services.AddSingleton<IActivityStatisticsReader, RemoteActivityStatisticsReader>();
        builder.Services.AddSingleton<INotificationSettingsService, RemoteNotificationSettingsService>();
        builder.Services.AddSingleton<IUserPreferencesService, MauiApiUserPreferencesService>();
        builder.Services.AddSingleton<MauiDailyReminderService>();
        // Scoped: UserNotifier -> ISnackbar uses NavigationManager, which is only valid inside the Blazor WebView scope (not root/singleton).
        builder.Services.AddScoped<IUserNotifier, UserNotifier>();
        builder.Services.AddScoped<IFocusTimerClientAlerts, FocusTimerClientAlerts>();
        builder.Services.AddScoped<IDailyRetroPromptStore, JsDailyRetroPromptStore>();
        builder.Services.AddScoped<IInitialBoardLoadGate>(sp =>
            new InitialBoardLoadGate(sp.GetRequiredService<MauiInitialBoardLoadSignal>()));
        builder.Services.AddScoped<IUserTimeZoneService, UserTimeZoneService>();
        builder.Services.AddScoped<INotificationSettingsRules, NotificationSettingsRules>();
        builder.Services.AddSingleton<IUserDateFormatService, UserDateFormatService>();
        builder.Services.AddSingleton<IAccountActionsService, RemoteAccountActionsService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void AddEmbeddedAppSettings(MauiAppBuilder builder)
    {
        System.Reflection.Assembly assembly = typeof(MauiProgram).Assembly;
        AddEmbeddedJsonIfPresent(assembly, builder, "appsettings.json");
        AddEmbeddedJsonIfPresent(assembly, builder, "appsettings.Release.json");
    }

    private static void AddEmbeddedJsonIfPresent(System.Reflection.Assembly assembly, MauiAppBuilder builder,
        string suffix)
    {
        string? resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            return;
        }

        using System.IO.Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            builder.Configuration.AddJsonStream(stream);
        }
    }
}
