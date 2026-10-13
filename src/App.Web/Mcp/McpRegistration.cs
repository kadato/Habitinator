namespace App.Web.Mcp;

internal static class McpRegistration
{
    public static IServiceCollection AddHabitinatorMcp(this IServiceCollection services)
    {
        services.AddMcpServer(options =>
        {
            options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
            {
                Name = "habitinator",
                Title = "Habitinator board helper",
                Version = "1.0.0",
            };
            options.ServerInstructions =
                "Habitinator board helper. Authenticate with a JWT bearer token or auth cookie. " +
                "Before any mutation, read with get_snapshot or search_items so you have item ids. " +
                "Habits move with habit_plus and habit_minus. Past dailies complete with daily_complete_for_date. " +
                "Today toggles with toggle_item. Past daily completion is append-only. No tool reverses a past daily completion. " +
                "Write dates as yyyy-MM-dd and times as HH:mm. The server rejects bad values with InvalidParams. " +
                "Fix the input and retry. Reuse eventId on retry for log_activity and log_timer_session so the server logs the event once. " +
                "Confirm delete_item with the item id. Confirm import_data with CONFIRM.";
        })
        .WithHttpTransport()
        .WithToolsFromAssembly()
        .WithPromptsFromAssembly()
        .WithResourcesFromAssembly()
        .WithRequestFilters(b =>
        {
#pragma warning disable S2139 // Filters must log and rethrow so the protocol returns the error to the caller.
            b.AddCallToolFilter(next => async (ctx, ct) =>
            {
                var logger = GetLogger(ctx.Server.Services);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    return await next(ctx, ct);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "MCP tool {Tool} failed", ctx.Params.Name);
                    throw;
                }
                finally
                {
                    sw.Stop();
                    logger?.LogInformation("MCP tool {Tool} took {Ms}ms", ctx.Params.Name, sw.ElapsedMilliseconds);
                }
            });
            b.AddGetPromptFilter(next => async (ctx, ct) =>
            {
                var logger = GetLogger(ctx.Server.Services);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    return await next(ctx, ct);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "MCP prompt {Prompt} failed", ctx.Params.Name);
                    throw;
                }
                finally
                {
                    sw.Stop();
                    logger?.LogInformation("MCP prompt {Prompt} took {Ms}ms", ctx.Params.Name, sw.ElapsedMilliseconds);
                }
            });
            b.AddReadResourceFilter(next => async (ctx, ct) =>
            {
                var logger = GetLogger(ctx.Server.Services);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    return await next(ctx, ct);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "MCP resource {Uri} failed", ctx.Params.Uri);
                    throw;
                }
                finally
                {
                    sw.Stop();
                    logger?.LogInformation("MCP resource {Uri} took {Ms}ms", ctx.Params.Uri, sw.ElapsedMilliseconds);
                }
            });
#pragma warning restore S2139
        });

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

    private static ILogger? GetLogger(IServiceProvider? services)
    {
        var factory = services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        return factory?.CreateLogger("Habitinator.Mcp");
    }
}
