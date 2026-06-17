using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using App.Shared.RCL.Services;
using App.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Suppress verbose HttpClient logs (only show warnings/errors)
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Authorization", LogLevel.Warning);

// Register MudBlazor services
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomLeft;
    config.SnackbarConfiguration.ShowTransitionDuration = 250;
    config.SnackbarConfiguration.HideTransitionDuration = 200;
    config.SnackbarConfiguration.NewestOnTop = true;
});

// Configure Named HttpClient "api" referencing the host's base URL
var baseUri = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddHttpClient("api", client => client.BaseAddress = baseUri);

// Register platform abstractions
builder.Services.AddSingleton<ILocalSettingsStore, WasmLocalSettingsStore>();
builder.Services.AddScoped<IClientSessionProvider, WasmClientSessionProvider>();

// Register authentication state providers
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, WasmAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState();

// Register application services
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<GlobalTimerService>();
builder.Services.AddScoped<ITimerSessionLogService, TimerSessionLogService>();
builder.Services.AddScoped<RemoteBoardRefreshService>();
builder.Services.AddScoped<IRemoteBoardRefreshService>(sp => sp.GetRequiredService<RemoteBoardRefreshService>());
builder.Services.AddSingleton<IBoardSyncStatus, NoOpBoardSyncStatus>();
builder.Services.AddSingleton<ConflictResolutionService>();
builder.Services.AddScoped<BoardRemoteNotifyBridge>();
builder.Services.AddScoped<IInitialBoardLoadGate, InitialBoardLoadGate>();
builder.Services.AddScoped<IUndoService, UndoService>();

builder.Services.AddScoped<RemoteBoardDataService>();
builder.Services.AddScoped<IBoardDataService>(sp =>
{
    var inner = sp.GetRequiredService<RemoteBoardDataService>();
    var undoService = sp.GetRequiredService<IUndoService>();
    return new UndoableBoardDataService(inner, undoService);
});

builder.Services.AddScoped<IActivityStatisticsReader, RemoteActivityStatisticsReader>();
builder.Services.AddScoped<IUserActivityLogService, RemoteUserActivityLogService>();
builder.Services.AddScoped<INotificationSettingsService, RemoteNotificationSettingsService>();
builder.Services.AddScoped<IUserPreferencesService, WasmUserPreferencesService>();
builder.Services.AddScoped<IUserNotifier, UserNotifier>();
builder.Services.AddScoped<IFocusTimerClientAlerts, FocusTimerClientAlerts>();
builder.Services.AddScoped<IDailyRetroPromptStore, JsDailyRetroPromptStore>();
builder.Services.AddScoped<IUserTimeZoneService, UserTimeZoneService>();
builder.Services.AddScoped<INotificationSettingsRules, NotificationSettingsRules>();
builder.Services.AddScoped<IUserDateFormatService, UserDateFormatService>();
builder.Services.AddScoped<IAccountActionsService, RemoteAccountActionsService>();

var host = builder.Build();
try
{
    var js = host.Services.GetRequiredService<IJSRuntime>();
    await js.InvokeVoidAsync("habitinatorSetWasmLoaded");
}
catch
{
    // Safeguard
}
await host.RunAsync();
