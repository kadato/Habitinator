using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace App.Web.IntegrationTests;

/// <summary>Shared factory: PostgreSQL in Docker + web app with the same provider as production.</summary>
public sealed class PostgresWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await WaitUntilDatabaseAcceptsConnectionsAsync();
    }

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

    private async Task WaitUntilDatabaseAcceptsConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var lastError = (Exception?)null;
        const int maxAttempts = 20;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
                await connection.OpenAsync(cancellationToken);
                await connection.CloseAsync();
                return;
            }
            catch (NpgsqlException ex) when (attempt < maxAttempts)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }

        throw new InvalidOperationException("PostgreSQL container did not become ready in time.", lastError);
    }
}
