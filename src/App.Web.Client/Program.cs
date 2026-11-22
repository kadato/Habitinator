using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;
using App.Web.Client.Services;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Suppress verbose HttpClient logs, only show warnings and errors
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Authorization", LogLevel.Warning);

// Register MudBlazor services
builder.Services.AddHabitinatorMudServices();

// Configure Named HttpClient "api" referencing the host's base URL
Uri baseUri = new(builder.HostEnvironment.BaseAddress);
builder.Services.AddHttpClient("api", client => client.BaseAddress = baseUri);

// Register platform abstractions
builder.Services.AddSingleton<ILocalSettingsStore, WasmLocalSettingsStore>();
builder.Services.AddScoped<IClientSessionProvider, ClientSessionProvider>();
builder.Services.AddScoped<ICurrentUserKeyProvider, WasmCurrentUserKeyProvider>();
builder.Services.AddScoped<IBoardLocalStore, IndexedDbBoardLocalStore>();

// Register authentication state providers
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, WasmAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState();

// Shared client services. WebAssembly scopes live for the app lifetime.
builder.Services.AddHabitinatorClientCore(
    ServiceLifetime.Scoped,
    isOfflineProbe: sp => sp.GetRequiredService<IBoardLocalStore>() is IndexedDbBoardLocalStore wasm && !wasm.IsOnline());

// Platform-only services.
builder.Services.AddSingleton<IAppWindowProgressService, FallbackAppWindowProgressService>();
builder.Services.AddSingleton<IAppUpdaterService, FallbackAppUpdaterService>();

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
