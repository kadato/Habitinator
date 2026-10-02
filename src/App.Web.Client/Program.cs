using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;
using App.Shared.RCL.Services.CommandPalette;
using App.Shared.RCL.Services.Remote;
using App.Web.Client.Services;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

using MudBlazor;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Suppress verbose HttpClient logs, only show warnings and errors
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Authorization", LogLevel.Warning);

// Register MudBlazor services
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomLeft;
    config.SnackbarConfiguration.ShowTransitionDuration = 100;
    config.SnackbarConfiguration.HideTransitionDuration = 100;
    config.SnackbarConfiguration.NewestOnTop = true;
});

// Configure Named HttpClient "api" referencing the host's base URL
Uri baseUri = new(builder.HostEnvironment.BaseAddress);
builder.Services.AddHttpClient("api", client => client.BaseAddress = baseUri);

// Register platform abstractions
builder.Services.AddSingleton<ILocalSettingsStore, WasmLocalSettingsStore>();
builder.Services.AddSingleton<IActivityEventStore, SettingsActivityEventStore>();
builder.Services.AddSingleton<OfflineActivityStatisticsProvider>();
builder.Services.AddScoped<IClientSessionProvider, ClientSessionProvider>();

// Register authentication state providers
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, WasmAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState();

// Register application services
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<GlobalTimerService>();
builder.Services.AddSingleton<IAppWindowProgressService, FallbackAppWindowProgressService>();
builder.Services.AddSingleton<IAppUpdaterService, FallbackAppUpdaterService>();
builder.Services.AddScoped<ITimerSessionLogService, TimerSessionLogService>();
builder.Services.AddScoped<RemoteBoardRefreshService>();
builder.Services.AddScoped<IRemoteBoardRefreshService, SyncBeforeNotifyRefreshService>();
builder.Services.AddScoped<BoardRemoteNotifyBridge>();
builder.Services.AddScoped<IInitialBoardLoadGate>(sp => new InitialBoardLoadGate(sp.GetRequiredService<BoardInitialLoadSignal>()));
builder.Services.AddScoped<IUndoService, UndoService>();

builder.Services.AddScoped<RemoteBoardDataService>();
builder.Services.AddScoped<IBoardLocalStore, IndexedDbBoardLocalStore>();
builder.Services.AddScoped<ICurrentUserKeyProvider, WasmCurrentUserKeyProvider>();
builder.Services.AddScoped<BoardSyncStatus>();
builder.Services.AddScoped<IBoardSyncStatus>(sp => sp.GetRequiredService<BoardSyncStatus>());
builder.Services.AddScoped<BoardInitialLoadSignal>();
builder.Services.AddScoped<LocalFirstBoardDataService>();
builder.Services.AddScoped<IBoardLocalStoreLifecycle>(sp => sp.GetRequiredService<LocalFirstBoardDataService>());
builder.Services.AddScoped<BoardSyncCoordinator>(sp => new BoardSyncCoordinator(
    sp.GetRequiredService<LocalFirstBoardDataService>(),
    sp.GetRequiredService<ICurrentUserKeyProvider>(),
    sp.GetRequiredService<BoardSyncStatus>(),
    sp.GetRequiredService<BoardInitialLoadSignal>(),
    sp.GetRequiredService<RemoteBoardRefreshService>(),
    sp.GetRequiredService<ILogger<BoardSyncCoordinator>>(),
    sp,
    isOfflineProbe: () => sp.GetRequiredService<IBoardLocalStore>() is IndexedDbBoardLocalStore wasm && !wasm.IsOnline()));
builder.Services.AddScoped<IBoardSyncRequestor>(sp => sp.GetRequiredService<BoardSyncCoordinator>());
builder.Services.AddScoped<IBoardDataService>(sp =>
{
    var inner = sp.GetRequiredService<LocalFirstBoardDataService>();
    var undoService = sp.GetRequiredService<IUndoService>();
    return new UndoableBoardDataService(inner, undoService);
});

builder.Services.AddScoped<IActivityStatisticsReader, RemoteActivityStatisticsReader>();
builder.Services.AddScoped<IUserActivityLogService, RemoteUserActivityLogService>();
builder.Services.AddScoped<INotificationSettingsService, RemoteNotificationSettingsService>();
builder.Services.AddScoped<IUserPreferencesService, LocalFirstUserPreferencesService>();
builder.Services.AddScoped<IUserNotifier, UserNotifier>();
builder.Services.AddScoped<IFocusTimerClientAlerts, FocusTimerClientAlerts>();
builder.Services.AddScoped<IDailyRetroPromptStore, JsDailyRetroPromptStore>();
builder.Services.AddScoped<IUserTimeZoneService, UserTimeZoneService>();
builder.Services.AddScoped<INotificationSettingsRules, NotificationSettingsRules>();
builder.Services.AddScoped<IUserDateFormatService, UserDateFormatService>();
builder.Services.AddScoped<IAccountActionsService, RemoteAccountActionsService>();
builder.Services.AddScoped<IUserDataExportService, RemoteUserDataExportService>();
builder.Services.AddScoped<IUserDataImportService, RemoteUserDataImportService>();
builder.Services.AddScoped<IBoardColumnStateStore, JsBoardColumnStateStore>();
builder.Services.AddScoped<IOnboardingStore, JsOnboardingStore>();
builder.Services.AddScoped<BoardUiSessionState>();
builder.Services.AddScoped<ICommandPaletteService, CommandPaletteService>();

var host = builder.Build();
try
{
    var js = host.Services.GetRequiredService<IJSRuntime>();
    await js.InvokeVoidAsync("habUpdateProgress", "Starting application\u2026", 90);
    await js.InvokeVoidAsync("habitinatorSetWasmLoaded");
}
catch
{
    // Safeguard
}
await host.RunAsync();
