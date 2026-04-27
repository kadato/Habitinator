using System.Net;

namespace App.Web.IntegrationTests;

[Collection(nameof(IntegrationCollection))]
public sealed class OpenApiDocumentTests(PostgresWebAppFactory factory)
{
    [Fact]
    public async Task OpenApi_v1_json_is_available()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"openapi\"", json, StringComparison.Ordinal);
        Assert.Contains("/api/board", json, StringComparison.Ordinal);
    }
}
