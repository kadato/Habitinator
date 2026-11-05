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

var postgresUser = builder.AddParameter("postgres-user", "postgres");
var postgresPassword = builder.AddParameter("postgres-password", "postgres", secret: true);

var postgres = builder
    .AddPostgres("postgres", postgresUser, postgresPassword)
    .WithImage("library/postgres", "17.11")
    .WithDataVolume("habitinatordb-postgres-data")
    .WithPgAdmin()
    .WithRepl();

var habitinatorDb = postgres.AddDatabase("habitinatordb");

// To test on a physical device set HABITINATOR_HTTP_PORT to 5050. The setting keeps
// the port stable across runs. The Android tooling forwards the port with adb reverse,
// and the app uses a fixed localhost URL. If unset, Aspire picks a free port as before.
int? httpPort = null;
if (int.TryParse(Environment.GetEnvironmentVariable("HABITINATOR_HTTP_PORT"), out var parsedPort)
    && parsedPort is > 0 and <= 65535)
{
    httpPort = parsedPort;
}

// Aspire assigns a free port, so local runs never fight over fixed ports.
// Aspire defaults to a DCP reverse proxy in front of project endpoints. That breaks Blazor and SignalR WebSockets for many setups.
// Turn off the proxy so the browser and MAUI talk to Kestrel directly. See /health for orchestration.
var appWeb = builder.AddProject<Projects.App_Web>("app-web")
    .WithReference(habitinatorDb)
    .WaitFor(habitinatorDb)
    .WithHttpHealthCheck("/health")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithHttpEndpoint(port: httpPort, name: "http", isProxied: false);

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
