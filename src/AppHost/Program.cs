var builder = DistributedApplication.CreateBuilder(args);

var postgresUser = builder.AddParameter("postgres-user", "postgres");
var postgresPassword = builder.AddParameter("postgres-password", "postgres", secret: true);

var postgres = builder
    .AddPostgres("postgres", postgresUser, postgresPassword, 5432)
    .WithImage("library/postgres", "17.6")
    .WithDataVolume("habitinatordb-postgres-data")
    .WithPgAdmin();

var habitinatorDb = postgres.AddDatabase("habitinatordb");

// Port 5031 comes from App.Web Properties/launchSettings.json profile "http" (Kestrel binds there when proxy is off).
// Aspire defaults to a DCP reverse proxy in front of project endpoints; that breaks Blazor/SignalR WebSockets for many setups.
// Turn off the proxy so the browser and MAUI talk to Kestrel directly (see /health for orchestration).
var appWeb = builder.AddProject("app-web", "../App.Web/App.Web.csproj", options => options.LaunchProfileName = "http")
    .WithReference(habitinatorDb)
    .WaitFor(habitinatorDb)
    .WithHttpHealthCheck("/health")
    .WithEndpoint("http", static endpoint => endpoint.IsProxied = false);

// MAUI reads HABITINATOR_API_BASE_URL first (see MauiAppSettings). This env var is only set when
// the MAUI process is *started by this AppHost* (not when you F5 the MAUI project alone). Use
// AppHost as the startup project so app-web, postgres, and app-maui start together. Otherwise run
// App.Web yourself on port 5031, or set HABITINATOR_API_BASE_URL / Api:BaseUrl in MAUI. WaitFor
// app-web so login/board calls succeed after the API is up.
builder.AddProject("app-maui", "../App.MAUI/App.MAUI.csproj")
    .WithReference(habitinatorDb)
    .WaitFor(habitinatorDb)
    .WaitFor(appWeb)
    .WithEnvironment("HABITINATOR_API_BASE_URL", appWeb.GetEndpoint("http"));

builder.Build().Run();
