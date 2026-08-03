using App.Web.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Testcontainers.PostgreSql;

namespace App.Web.IntegrationTests;

/// <summary>Shared factory: PostgreSQL in Docker + web app with the same provider as production.</summary>
public sealed class PostgresWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string? _externalConnectionString =
        Environment.GetEnvironmentVariable("APPWEB_INTEGRATIONTESTS_CONNECTION_STRING");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithReuse(true)
        .Build();

    public async Task InitializeAsync()
    {
        var connectionString = _externalConnectionString;
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            await WaitUntilDatabaseAcceptsConnectionsAsync(
                PostgresResilienceConnectionString.EnsureColdStartTimeouts(connectionString));
            return;
        }

        await _postgres.StartAsync();
        await WaitUntilDatabaseAcceptsConnectionsAsync(
            PostgresResilienceConnectionString.EnsureColdStartTimeouts(_postgres.GetConnectionString()));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();

        if (string.IsNullOrWhiteSpace(_externalConnectionString))
        {
            await _postgres.DisposeAsync();
        }
    }

    /// <summary>
    /// With minimal hosting, <see cref="ConfigureWebHost" /> app configuration runs after
    /// <c>Program.cs</c> already read <see cref="Microsoft.Extensions.Configuration.IConfiguration" />
    /// for EF registration. Host configuration is merged early enough for connection strings to apply.
    /// See https://github.com/dotnet/aspnetcore/issues/37680
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(GetIntegrationConfiguration()));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(GetIntegrationConfiguration()));
    }

    private Dictionary<string, string?> GetIntegrationConfiguration()
    {
        var connectionString = PostgresResilienceConnectionString.EnsureColdStartTimeouts(
            string.IsNullOrWhiteSpace(_externalConnectionString)
                ? _postgres.GetConnectionString()
                : _externalConnectionString);

        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["ConnectionStrings:habitinatordb"] = connectionString,
            ["Jwt:Issuer"] = "Habitinator",
            ["Jwt:Audience"] = "HabitinatorClients",
            ["Jwt:SigningKey"] =
                "integration-test-signing-key-must-be-long-enough-for-hmac-sha256-validation-0123456789",
            ["Jwt:ExpirationMinutes"] = "60",
            ["DemoUser:Email"] = "guest@habitinator.local",
            ["DemoUser:Password"] = "Guest123!",
        };
    }

    private static async Task WaitUntilDatabaseAcceptsConnectionsAsync(string connectionString,
        CancellationToken cancellationToken = default)
    {
        connectionString = PostgresResilienceConnectionString.EnsureColdStartTimeouts(connectionString);
        Exception? lastError = null;
        const int maxAttempts = 60;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using NpgsqlConnection connection = new(connectionString);
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
