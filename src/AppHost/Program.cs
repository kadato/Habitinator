var builder = DistributedApplication.CreateBuilder(args);

var postgresUser = builder.AddParameter("postgres-user", "postgres");
var postgresPassword = builder.AddParameter("postgres-password", "postgres", secret: true);

var postgres = builder
    .AddPostgres("postgres", postgresUser, postgresPassword, 5432)
    .WithImage("library/postgres", "17.6")
    .WithDataVolume("habitinatordb-postgres-data")
    .WithPgAdmin();

var habitinatorDb = postgres.AddDatabase("habitinatordb");

builder.AddProject("app-web", "../App.Web/App.Web.csproj")
    .WithReference(habitinatorDb)
    .WaitFor(habitinatorDb);

builder.AddProject("app-maui", "../App.MAUI/App.MAUI.csproj")
    .WithReference(habitinatorDb)
    .WaitFor(habitinatorDb);

builder.Build().Run();
