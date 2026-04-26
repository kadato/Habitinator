using System;
using System.Linq;
using System.Reflection;
using App.MAUI.Services;
using App.Shared.RCL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
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
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});
		builder.UseLocalNotification(config =>
		{
			_ = config.AddAndroid(a => a.AddChannel(new AndroidNotificationChannelRequest
			{
				Id = MauiDailyReminderService.AndroidChannelId,
				Name = "Daily reminder",
				Description = "Dailies due and to-dos with deadlines",
			}));
		});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddMudServices();
		string apiBase = MauiAppSettings.ResolveApiBaseUrl(builder.Configuration).TrimEnd('/') + "/";
		builder.Services.AddSingleton(_ => new MauiApiEndpointOptions(apiBase));
		builder.Services.AddSingleton<IAuthTokenStore, AuthTokenStore>();
		builder.Services.AddSingleton<IRemoteBoardRefreshService, RemoteBoardRefreshService>();
		builder.Services.AddSingleton<BoardRemoteNotifyBridge>();
		builder.Services.AddSingleton<MauiBoardHubService>();
		builder.Services.AddSingleton<IApiSession, ApiSession>();
		builder.Services.AddTransient<AuthMessageHandler>();
		builder.Services.AddTransient<ClearSessionOnUnauthorizedHandler>();
		builder.Services.AddHttpClient("apiAuth", c => c.BaseAddress = new Uri(apiBase));
		builder.Services.AddHttpClient("api", c => c.BaseAddress = new Uri(apiBase))
			.AddHttpMessageHandler<AuthMessageHandler>()
			.AddHttpMessageHandler<ClearSessionOnUnauthorizedHandler>();
		builder.Services.AddSingleton<ApiAuthService>();
		builder.Services.AddSingleton<RemoteBoardDataService>();
		builder.Services.AddSingleton<IClock, SystemClock>();
		builder.Services.AddSingleton<GlobalTimerService>();
		builder.Services.AddSingleton<MauiActivityEventStore>();
		builder.Services.AddSingleton<IUserActivityLogService, MauiUserActivityLogService>();
		builder.Services.AddSingleton<IBoardDataService>(sp =>
		{
			RemoteBoardDataService inner = sp.GetRequiredService<RemoteBoardDataService>();
			IUserActivityLogService log = sp.GetRequiredService<IUserActivityLogService>();
			return new ActivityLoggingBoardDataService(inner, log);
		});
		builder.Services.AddSingleton<IActivityStatisticsReader, MauiApiActivityStatisticsReader>();
		builder.Services.AddSingleton<INotificationSettingsService, MauiApiNotificationSettingsService>();
		builder.Services.AddSingleton<MauiDailyReminderService>();
		// Scoped: UserNotifier -> ISnackbar uses NavigationManager, which is only valid inside the Blazor WebView scope (not root/singleton).
		builder.Services.AddScoped<IUserNotifier, UserNotifier>();
		builder.Services.AddScoped<IFocusTimerClientAlerts, FocusTimerClientAlerts>();
		builder.Services.AddScoped<IDailyRetroPromptStore, JsDailyRetroPromptStore>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	private static void AddEmbeddedAppSettings(MauiAppBuilder builder)
	{
		Assembly assembly = typeof(MauiProgram).Assembly;
		string? resName = assembly
			.GetManifestResourceNames()
			.FirstOrDefault(static n => n.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase));
		if (resName is null)
		{
			return;
		}

		using Stream? stream = assembly.GetManifestResourceStream(resName);
		if (stream is not null)
		{
			builder.Configuration.AddJsonStream(stream);
		}
	}
}
