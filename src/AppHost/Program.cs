using Aspire.Hosting.Lifecycle;

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://localhost:15000");
}
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL")))
{
    Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", "http://localhost:19000");
}
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL")))
{
    Environment.SetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", "http://localhost:20000");
}
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT")))
{
    Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");
}

var builder = DistributedApplication.CreateBuilder(args);

// Aspire 17 adds -mt to the coordinated .NET build on .NET 11 SDKs. The WebAssembly
// SDK's ReadWasmProps task uses a custom task factory, which cannot run in the
// out-of-process task host that -mt uses. Remove the subscriber until Aspire stops
// adding -mt or the WebAssembly SDK supports it.
var multiThreadedBuildSubscriber = builder.Services.FirstOrDefault(descriptor =>
    descriptor.ServiceType == typeof(IDistributedApplicationEventingSubscriber) &&
    descriptor.ImplementationType?.Name == "DotnetBuildCommandEventingSubscriber");
if (multiThreadedBuildSubscriber is not null)
{
    builder.Services.Remove(multiThreadedBuildSubscriber);
}

var postgresUser = builder.AddParameter("postgres-user", "postgres");
var postgresPassword = builder.AddParameter("postgres-password", "postgres", secret: true);

var postgres = builder
    .AddPostgres("postgres", postgresUser, postgresPassword)
    .WithImage("library/postgres", "17.11")
    .WithDataVolume("habitinatordb-postgres-data")
    .WithPgAdmin()
    .WithRepl();

var habitinatorDb = postgres.AddDatabase("habitinatordb");

// To test on a physical device, set HABITINATOR_HTTP_PORT to a stable port outside
// the Windows excluded port ranges. 5150 works on WSL. 5050 is inside a common WSL
// exclusion. The setting keeps the port stable across runs. The Android tooling
// forwards the port with adb reverse, and the app uses a fixed localhost URL.
// If the variable is unset, Aspire picks a free port as before.
int? httpPort = null;
if (int.TryParse(Environment.GetEnvironmentVariable("HABITINATOR_HTTP_PORT"), out var parsedPort)
    && parsedPort is > 0 and <= 65535)
{
    httpPort = parsedPort;
}

// Aspire assigns a free port, so local runs never fight over fixed ports.
// Aspire defaults to a DCP reverse proxy in front of project endpoints. That breaks Blazor and SignalR WebSockets for many setups.
// Turn off the proxy so the browser and MAUI talk to Kestrel directly. See /health for orchestration.
// App.Web's launch profile pins ASPNETCORE_URLS to localhost:5050. That port is
// inside a Windows excluded range on WSL, so the app fails to bind before Aspire
// can apply the dynamic endpoint. Ignore the profile and let the endpoint decide.
var appWeb = builder.AddDotnetProject(
        "app-web",
        "../App.Web/App.Web.csproj",
        options => options.ExcludeLaunchProfile = true)
    .WithReference(habitinatorDb)
    .WaitFor(habitinatorDb)
    .WithHttpEndpoint(port: httpPort, name: "http", isProxied: false)
    .WithHttpHealthCheck("/health")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

// Use dotnet run so the dashboard Start button actually builds and launches the MAUI app.
var mauiDir = Path.Combine(builder.AppHostDirectory, "..", "App.MAUI");

// The Windows target runs only on Windows hosts.
// On Linux and macOS the dotnet process exits with NETSDK1100.
// The dashboard then shows Finished with no app launch.
if (OperatingSystem.IsWindows())
{
    _ = builder.AddExecutable("app-maui-win", "dotnet", mauiDir,
            "run", "--project", "App.MAUI.csproj", "-f", "net11.0-windows10.0.19041.0")
        .WithExplicitStart()
        .WaitFor(habitinatorDb)
        .WaitFor(appWeb)
        .WithEnvironment("HABITINATOR_API_BASE_URL", appWeb.GetEndpoint("http"));
}

_ = builder.AddExecutable("app-maui-android-emulator", "dotnet", mauiDir,
        "run", "--project", "App.MAUI.csproj", "-f", "net11.0-android")
    .WithExplicitStart()
    .WaitFor(habitinatorDb)
    .WaitFor(appWeb)
    .WithEnvironment("HABITINATOR_API_BASE_URL", appWeb.GetEndpoint("http"));

// Physical-device deploy runs from the dashboard. The deploy runs tools/android-device.sh
// with the allocated backend port. The script skips the nested Aspire start. The script
// runs adb reverse. Next the script builds the app. Last the script deploys the app.
// The deploy runs on Bash. The deploy does not run on Windows.
if (!OperatingSystem.IsWindows())
{
    var toolsDir = Path.Combine(builder.AppHostDirectory, "..", "..", "tools");
    _ = builder.AddExecutable("app-maui-android-device", "bash", toolsDir,
            "android-device.sh")
        .WithExplicitStart()
        .WaitFor(habitinatorDb)
        .WaitFor(appWeb)
        .WithEnvironment("HABITINATOR_HTTP_PORT", appWeb.GetEndpoint("http").Property(EndpointProperty.Port));
}

await builder.Build().RunAsync();
