using App.Web.Services;

using FluentAssertions;

using Microsoft.Extensions.Configuration;

namespace App.Web.IntegrationTests;

public sealed class PostgresMigrationConnectionStringsTests
{
    [Fact]
    public void ResolveForMigrations_strips_neon_pooler_segment_from_host()
    {
        var pooled =
            "Host=ep-fake-123456-pooler.us-east-2.aws.neon.tech;Port=5432;Database=neondb;Username=u;Password=p;Ssl Mode=Require";
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = pooled
        }).Build();

        var migration = PostgresMigrationConnectionStrings.ResolveForMigrations(cfg);

        migration.Should().ContainEquivalentOf("Host=ep-fake-123456.us-east-2.aws.neon.tech");
        migration.Should().NotContainEquivalentOf("-pooler");
    }

    [Fact]
    public void ResolveForMigrations_prefers_MigrationConnection_over_derivation()
    {
        var pooled =
            "Host=ep-fake-123456-pooler.us-east-2.aws.neon.tech;Port=5432;Database=neondb;Username=u;Password=p;Ssl Mode=Require";
        var explicitDirect =
            "Host=ep-explicit.region.aws.neon.tech;Port=5432;Database=neondb;Username=u;Password=p;Ssl Mode=Require";
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = pooled,
            ["ConnectionStrings:MigrationConnection"] = explicitDirect
        }).Build();

        var migration = PostgresMigrationConnectionStrings.ResolveForMigrations(cfg);

        migration.Should().ContainEquivalentOf("ep-explicit.region.aws.neon.tech");
    }

    [Fact]
    public void ResolveForMigrations_leaves_non_neon_connection_string_unchanged()
    {
        var local = "Host=localhost;Port=5432;Database=habitinatordb;Username=postgres;Password=postgres";
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = local
        }).Build();

        var migration = PostgresMigrationConnectionStrings.ResolveForMigrations(cfg);

        migration.Should().Be(local);
    }
}
