using App.Web.Hubs;
using App.Web.Middleware;

namespace App.Web;

internal static class PipelineExtensions
{
    private const string TestingEnvironment = "Testing";

    internal static void ConfigurePipeline(this WebApplication app)
    {
        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment(TestingEnvironment))
        {
            app.UseExceptionHandler("/Error", true);
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        if (!app.Environment.IsEnvironment(TestingEnvironment))
        {
            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();
        }

        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<DiscoveryLinkHeadersMiddleware>();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        if (!app.Environment.IsEnvironment(TestingEnvironment))
        {
            app.UseRateLimiter();
        }

        // Used by AppHost WithHttpHealthCheck; anonymous, no auth required.
        app.MapGet("/health", () => Results.Text("OK", "text/plain"));

        app.MapGet("/.well-known/change-password", () => Results.Redirect("/settings", permanent: false));
        app.MapOpenApi();
        app.UseResponseCompression();
        app.MapStaticAssets();
        app.MapRazorComponents<App.Web.Components.App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(App.Web.Client._Imports).Assembly);

        app.MapHub<BoardHub>("/hubs/board").RequireRateLimiting("api");
    }
}
