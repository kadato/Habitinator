using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace App.Web.IntegrationTests;

/// <summary>Shared factory: PostgreSQL in Docker + web app with the same provider as production.</summary>
public sealed class PostgresWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Jwt:Issuer"] = "Habitinator",
                ["Jwt:Audience"] = "HabitinatorClients",
                ["Jwt:SigningKey"] =
                    "integration-test-signing-key-must-be-long-enough-for-hmac-sha256-validation-0123456789",
                ["Jwt:ExpirationMinutes"] = "60",
                ["DemoUser:Email"] = "guest@habitinator.local",
                ["DemoUser:Password"] = "Guest123!",
            });
        });
    }
}
