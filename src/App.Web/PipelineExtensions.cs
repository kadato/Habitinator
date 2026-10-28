using App.Shared.RCL.Models;
using App.Web.Hubs;
using App.Web.Mcp;
using App.Web.Middleware;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Web;

internal static class PipelineExtensions
{
    private const string PlainTextContentType = "text/plain";

    internal static void ConfigurePipeline(this WebApplication app)
    {
        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment(AppEnvironment.Testing))
        {
            app.UseExceptionHandler("/Error", true);
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        // Compression must run early so OpenAPI documents and middleware error bodies are also compressed.
        app.UseResponseCompression();

        if (!app.Environment.IsEnvironment(AppEnvironment.Testing))
        {
            // API clients need true status codes. A missing row returns 404, not a re-executed
            // page. Re-executing /api 404s as the /not-found page turns them into 405s.
            app.UseWhen(
                static ctx => !ctx.Request.Path.StartsWithSegments("/api"),
                static a => a.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
            app.UseHttpsRedirection();
        }

        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<DiscoveryLinkHeadersMiddleware>();
        app.UseMiddleware<StaticCacheHeadersMiddleware>();
        app.UseMiddleware<SameOriginCsrfMiddleware>();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        if (!app.Environment.IsEnvironment(AppEnvironment.Testing))
        {
            app.UseRateLimiter();
        }

        // Used by AppHost WithHttpHealthCheck and Azure health check. Anonymous, no auth required.
        // Liveness: process is up. Keep cloud probes here. Do not point them at
        // /health/ready since each DB check blocks Neon scale to zero.
        app.MapGet("/health", () => Results.Text("OK", PlainTextContentType));
        app.MapGet("/health/ready", async (IServiceProvider services, CancellationToken cancellationToken) =>
        {
            try
            {
                using var scope = services.CreateScope();
                var factory = scope.ServiceProvider.GetService<IDbContextFactory<App.Web.Data.ApplicationDbContext>>();
                if (factory is null)
                {
                    return Results.Text("OK", PlainTextContentType);
                }

                await using var db = await factory.CreateDbContextAsync(cancellationToken);
                var canConnect = await db.Database.CanConnectAsync(cancellationToken);
                return canConnect
                    ? Results.Text("OK", PlainTextContentType)
                    : Results.Text("DB unavailable", PlainTextContentType, statusCode: 503);
            }
            catch (Exception)
            {
                return Results.Text("DB unavailable", PlainTextContentType, statusCode: 503);
            }
        });

        app.MapGet("/.well-known/change-password", () => Results.Redirect("/settings", permanent: false));
        app.MapGet("/.well-known/mcp/server-card.json", (HttpContext http, IOptions<SitePublicOptions> site) =>
            Results.Json(McpServerCard.Build(http, site)))
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("api");
        app.MapGet("/.well-known/api-catalog", () =>
            Results.Content(ApiCatalogDocument.Json, "application/linkset+json"))
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("api");
        app.UseOpenApi(options =>
        {
            options.Path = "/openapi/v1.json";
        });
        app.MapStaticAssets();
        app.MapRazorComponents<App.Web.Components.App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(App.Web.Client._Imports).Assembly);

        app.MapHub<BoardHub>("/hubs/board").RequireRateLimiting("api");
    }
}
