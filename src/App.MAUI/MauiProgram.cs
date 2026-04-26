using Microsoft.Extensions.Logging;
using App.MAUI.Services;
using App.Shared.RCL.Services;
using MudBlazor.Services;

namespace App.MAUI;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddMudServices();
		builder.Services.AddSingleton<IClock, SystemClock>();
		builder.Services.AddSingleton<GlobalTimerService>();
		builder.Services.AddSingleton<MauiActivityEventStore>();
		builder.Services.AddSingleton<LocalBoardDataService>();
		builder.Services.AddSingleton<IUserActivityLogService, MauiUserActivityLogService>();
		builder.Services.AddSingleton<IBoardDataService>(sp =>
		{
			LocalBoardDataService inner = sp.GetRequiredService<LocalBoardDataService>();
			IUserActivityLogService log = sp.GetRequiredService<IUserActivityLogService>();
			return new ActivityLoggingBoardDataService(inner, log);
		});
		builder.Services.AddSingleton<IActivityStatisticsReader, MauiActivityStatisticsReader>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
