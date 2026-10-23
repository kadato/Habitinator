namespace App.Web;

/// <summary>Single source of truth for the RFC 9727 api-catalog document.</summary>
internal static class ApiCatalogDocument
{
    public const string Json = """
        {
          "linkset": [
            {
              "anchor": "https://habitinator.app/",
              "item": [
                {
                  "href": "https://habitinator.app/openapi/v1.json",
                  "rel": "openapi",
                  "type": "application/vnd.oai.openapi+json",
                  "title": "Habitinator HTTP API (OpenAPI 3)"
                },
                {
                  "href": "https://habitinator.app/health",
                  "rel": "health",
                  "type": "text/plain",
                  "title": "Liveness probe"
                },
                {
                  "href": "https://habitinator.app/llms.txt",
                  "rel": "alternate",
                  "type": "text/plain",
                  "title": "LLM site index"
                },
                {
                  "href": "https://habitinator.app/mcp",
                  "rel": "mcp",
                  "type": "application/json",
                  "title": "Habitinator MCP server (Streamable HTTP, Bearer auth)"
                },
                {
                  "href": "https://habitinator.app/.well-known/mcp/server-card.json",
                  "rel": "service-desc",
                  "type": "application/json",
                  "title": "Habitinator MCP server card"
                },
                {
                  "href": "https://habitinator.app/auth.md",
                  "rel": "service-doc",
                  "type": "text/markdown",
                  "title": "Agent registration and MCP token guide"
                },
                {
                  "href": "https://habitinator.app/.well-known/ai-catalog.json",
                  "rel": "ai-catalog",
                  "type": "application/json",
                  "title": "Agent capability catalog"
                },
                {
                  "href": "https://habitinator.app/.well-known/agent-skills/index.json",
                  "rel": "agent-skills",
                  "type": "application/json",
                  "title": "Agent skills index"
                }
              ]
            }
          ]
        }
        """;
}
