using App.LocalBoard.Sqlite;
using App.LocalBoard.Sqlite.Data;
using App.MAUI.Services;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
#pragma warning disable IDE0005 // Required by Android TFM, not auto-imported there
using Microsoft.Extensions.Logging;
#pragma warning restore IDE0005

using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models.AndroidOption;

namespace App.MAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        AddEmbeddedAppSettings(builder);
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Karla-Regular.ttf", "Karla");
                fonts.AddFont("Karla-Medium.ttf", "KarlaMedium");
                fonts.AddFont("Karla-Bold.ttf", "KarlaBold");
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
        builder.Services.AddHabitinatorMudServices();
        var apiBase = MauiAppSettings.ResolveApiBaseUrl(builder.Configuration).TrimEnd('/') + "/";
        builder.Services.AddSingleton(_ => new MauiApiEndpointOptions(apiBase));
        builder.Services.AddSingleton<IAuthTokenStore, AuthTokenStore>();
        builder.Services.AddSingleton<ILocalSettingsStore, MauiLocalSettingsStore>();
        builder.Services.AddSingleton<IClientSessionProvider>(sp => sp.GetRequiredService<IApiSession>());
        var localBoardDbPath = Path.Combine(FileSystem.AppDataDirectory, "habitinator-board-local.db");
        builder.Services.AddDbContextFactory<LocalBoardDbContext>(o =>
            o.UseSqlite($"Data Source={localBoardDbPath}"));
        builder.Services.AddSingleton<ICurrentUserKeyProvider, MauiCurrentUserKeyProvider>();
        builder.Services.AddSingleton<IBoardLocalStore, SqliteBoardLocalStore>();

        // Shared client services. MAUI singletons are needed because platform services
        // hold the board service for the app lifetime.
        builder.Services.AddHabitinatorClientCore(
            ServiceLifetime.Singleton,
            isOfflineProbe: _ =>
            {
                try
                {
                    return Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
                }
                catch
                {
                    return false;
                }
            });

        // Platform-only services.
        builder.Services.AddSingleton<MauiBoardHubService>();
        builder.Services.AddSingleton<MauiDailyReminderService>();
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
        builder.Services.AddSingleton<IApiSession, ApiSession>();

#if WINDOWS
        builder.Services.AddSingleton<IAppWindowProgressService, global::App.MAUI.Platforms.Windows.WindowsAppWindowProgressService>();
#else
        builder.Services.AddSingleton<IAppWindowProgressService, Services.MauiAppWindowProgressService>();
#endif
        builder.Services.AddOptions<MauiAppUpdaterOptions>()
            .BindConfiguration(MauiAppUpdaterOptions.SectionName);
        builder.Services.AddSingleton<IAppUpdaterService, Services.MauiAppUpdaterService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void AddEmbeddedAppSettings(MauiAppBuilder builder)
    {
        var assembly = typeof(MauiProgram).Assembly;
        AddEmbeddedJsonIfPresent(assembly, builder, "appsettings.json");
        // Platform-specific overrides. Only the Android file is embedded in the Android build.
        AddEmbeddedJsonIfPresent(assembly, builder, "appsettings.Android.json");
        AddEmbeddedJsonIfPresent(assembly, builder, "appsettings.Release.json");
    }

    private static void AddEmbeddedJsonIfPresent(System.Reflection.Assembly assembly, MauiAppBuilder builder,
        string suffix)
    {
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            return;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            builder.Configuration.AddJsonStream(stream);
        }
    }
}
