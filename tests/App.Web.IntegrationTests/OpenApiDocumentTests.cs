using System.Net;

using FluentAssertions;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class OpenApiDocumentTests(PostgresWebAppFactory factory)
{
    [Fact]
    public async Task OpenApi_v1_json_is_available()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/openapi/v1.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadAsStringAsync();
        json.Should().Contain("\"openapi\"");
        json.Should().Contain("/api/board");
        json.Should().Contain("/api/activity/overview");
    }

    [Fact]
    public async Task OpenApi_v1_json_covers_recent_routes()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/openapi/v1.json");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        json.Should().Contain("/api/auth/forgot-password");
        json.Should().Contain("/api/auth/reset-password");
        json.Should().Contain("skip-for-date");
        json.Should().Contain("/api/account/import");
        json.Should().Contain("/api/account/export");
    }
}
