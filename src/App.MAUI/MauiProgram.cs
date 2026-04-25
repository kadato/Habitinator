using Microsoft.Extensions.Logging;
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
		builder.Services.AddSingleton<IBoardDataService, LocalBoardDataService>();
		builder.Services.AddSingleton<IUserActivityLogService, NoOpUserActivityLogService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
