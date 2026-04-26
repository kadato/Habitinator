var builder = DistributedApplication.CreateBuilder(args);

var postgresUser = builder.AddParameter("postgres-user", "postgres");
var postgresPassword = builder.AddParameter("postgres-password", "postgres", secret: true);

var postgres = builder
    .AddPostgres("postgres", postgresUser, postgresPassword, 5432)
    .WithImage("library/postgres", "17.6")
    .WithDataVolume("habitinatordb-postgres-data")
    .WithPgAdmin();

var habitinatorDb = postgres.AddDatabase("habitinatordb");

// Port 5031 comes from App.Web Properties/launchSettings.json profile "http".
// Do not add WithHttpEndpoint(name: "http") — AddProject already creates that endpoint from the launch profile.
var appWeb = builder.AddProject("app-web", "../App.Web/App.Web.csproj", options => options.LaunchProfileName = "http")
    .WithReference(habitinatorDb)
    .WaitFor(habitinatorDb);

// MAUI reads HABITINATOR_API_BASE_URL first (see MauiAppSettings). WaitFor web so login/board calls succeed after the API is up.
builder.AddProject("app-maui", "../App.MAUI/App.MAUI.csproj")
    .WithReference(habitinatorDb)
    .WaitFor(habitinatorDb)
    .WaitFor(appWeb)
    .WithEnvironment("HABITINATOR_API_BASE_URL", appWeb.GetEndpoint("http"));

builder.Build().Run();
