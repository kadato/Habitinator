namespace App.Web.Mcp;

internal static class McpRegistration
{
    public static IServiceCollection AddHabitinatorMcp(this IServiceCollection services)
    {
        services.AddMcpServer(options =>
        {
            options.ServerInstructions =
                "Habitinator board helper. Read with get_snapshot or get_sync_delta first. " +
                "Use habit_plus and habit_minus for habits, daily_complete_for_date for dailies, toggle_item for todos. " +
                "Confirm delete_item and import_data before you call them.";
        })
        .WithHttpTransport()
        .WithToolsFromAssembly()
        .WithPromptsFromAssembly()
        .WithResourcesFromAssembly()
        .WithRequestFilters(b => b.AddCallToolFilter(next => async (ctx, ct) =>
        {
            var factory = ctx.Server.Services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
            var logger = factory?.CreateLogger("Habitinator.Mcp");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                return await next(ctx, ct);
            }
            finally
            {
                sw.Stop();
                logger?.LogInformation("MCP tool {Tool} took {Ms}ms", ctx.Params.Name, sw.ElapsedMilliseconds);
            }
        }));

        return services;
    }

    public static IEndpointRouteBuilder MapHabitinatorMcp(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp("/mcp")
            .DisableAntiforgery()
            .RequireAuthorization("BoardOrJwt")
            .RequireRateLimiting("api");
        return endpoints;
    }
}
