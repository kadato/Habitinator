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

// Explicitly set configuration fallbacks to prevent OTLP / ASPNETCORE_URLS dashboard startup failures
builder.Configuration["ASPNETCORE_URLS"] ??= "http://localhost:15000";
builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"] ??= "http://localhost:19000";
builder.Configuration["ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL"] ??= "http://localhost:20000";
builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] ??= "true";

var postgresUser = builder.AddParameter("postgres-user", "postgres");
var postgresPassword = builder.AddParameter("postgres-password", "postgres", secret: true);

var postgres = builder
    .AddPostgres("postgres", postgresUser, postgresPassword, 5432)
    .WithImage("library/postgres", "17.6")
    .WithDataVolume("habitinatordb-postgres-data")
    .WithPgAdmin();

var habitinatorDb = postgres.AddDatabase("habitinatordb");

// Port 5033 comes from App.Web Properties/launchSettings.json profile "http" (Kestrel binds there when proxy is off).
// Aspire defaults to a DCP reverse proxy in front of project endpoints; that breaks Blazor/SignalR WebSockets for many setups.
// Turn off the proxy so the browser and MAUI talk to Kestrel directly (see /health for orchestration).
var appWeb = builder.AddProject("app-web", "../App.Web/App.Web.csproj", options => options.LaunchProfileName = "http")
    .WithReference(habitinatorDb)
    .WaitFor(habitinatorDb)
    .WithHttpHealthCheck("/health")
    .WithEndpoint("http", static endpoint => endpoint.IsProxied = false);

// MAUI reads HABITINATOR_API_BASE_URL first (see MauiAppSettings). This env var is only set when
// the MAUI process is *started from the Aspire dashboard* (or F5 on App.MAUI alone). AppHost starts
// postgres and app-web automatically; start app-maui manually when you need the hybrid client.
// Otherwise run App.Web yourself on port 5033, or set HABITINATOR_API_BASE_URL / Api:BaseUrl in MAUI.
builder.AddProject("app-maui", "../App.MAUI/App.MAUI.csproj")
    .WithExplicitStart()
    .WithReference(habitinatorDb)
    .WaitFor(habitinatorDb)
    .WaitFor(appWeb)
    .WithEnvironment("HABITINATOR_API_BASE_URL", appWeb.GetEndpoint("http"));

builder.Build().Run();
