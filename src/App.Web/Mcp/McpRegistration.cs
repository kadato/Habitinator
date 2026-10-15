using App.Web.Auth;
using App.Web.Services;

using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace App.Web.Mcp;

internal static class McpRegistration
{
    public static IServiceCollection AddHabitinatorMcp(this IServiceCollection services)
    {
        var version = typeof(McpRegistration).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        services.AddMcpServer(options =>
        {
            options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
            {
                Name = "habitinator",
                Title = "Habitinator board helper",
                Version = version,
            };
            options.ServerInstructions =
                "Habitinator board helper. Authenticate with a JWT bearer token or auth cookie. " +
                "Before any mutation, read with get_snapshot or search_items so you have item ids. " +
                "Habits move with habit_plus and habit_minus. Past dailies complete with daily_complete_for_date. " +
                "Today toggles with toggle_item. Past daily completion is append-only. No tool reverses a past daily completion. " +
                "Write dates as yyyy-MM-dd and times as HH:mm. The server rejects bad values with InvalidParams. " +
                "Fix the input and retry. Reuse eventId on retry for log_activity and log_timer_session so the server logs the event once. " +
                "delete_item and import_data ask for elicitation confirmation. Older clients pass confirmToken instead.";
        })
        .WithHttpTransport(o =>
        {
            o.Stateless = true;
            o.SessionMode = HttpServerSessionMode.Stateless;
        })
        .WithToolsFromAssembly()
        .WithPromptsFromAssembly()
        .WithResourcesFromAssembly()
        .WithCompleteHandler(HandleCompleteAsync)
        .WithRequestFilters(b =>
        {
#pragma warning disable S2139 // Filters must log and rethrow so the protocol returns the error to the caller.
            b.AddCallToolFilter(next => async (ctx, ct) =>
            {
                var logger = GetLogger(ctx.Server.Services);
                var userId = AuthenticatedUserId.TryGet(ctx.User)?.ToString() ?? "anon";
                var argCount = ctx.Params.Arguments?.Count ?? 0;
                using var activity = AppTelemetry.Activity.StartActivity("mcp.tool");
                activity?.SetTag("mcp.tool", ctx.Params.Name);
                activity?.SetTag("mcp.arg_count", argCount);
                activity?.SetTag("user.id", userId);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var result = await next(ctx, ct);
                    AppTelemetry.RecordMcpTool(ctx.Params.Name, "ok", sw.ElapsedMilliseconds);
                    logger?.LogInformation("MCP tool {Tool} user {User} args {Args} took {Ms}ms",
                        ctx.Params.Name, userId, argCount, sw.ElapsedMilliseconds);
                    return result;
                }
                catch (InputRequiredException)
                {
                    AppTelemetry.RecordMcpTool(ctx.Params.Name, "elicitation", sw.ElapsedMilliseconds);
                    throw;
                }
                catch (Exception ex)
                {
                    AppTelemetry.RecordMcpTool(ctx.Params.Name, "error", sw.ElapsedMilliseconds);
                    logger?.LogError(ex, "MCP tool {Tool} failed", ctx.Params.Name);
                    throw;
                }
            });
            b.AddGetPromptFilter(next => async (ctx, ct) =>
            {
                var logger = GetLogger(ctx.Server.Services);
                var userId = AuthenticatedUserId.TryGet(ctx.User)?.ToString() ?? "anon";
                using var activity = AppTelemetry.Activity.StartActivity("mcp.prompt");
                activity?.SetTag("mcp.prompt", ctx.Params.Name);
                activity?.SetTag("user.id", userId);
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
                    logger?.LogInformation("MCP prompt {Prompt} user {User} took {Ms}ms",
                        ctx.Params.Name, userId, sw.ElapsedMilliseconds);
                }
            });
            b.AddReadResourceFilter(next => async (ctx, ct) =>
            {
                var logger = GetLogger(ctx.Server.Services);
                var userId = AuthenticatedUserId.TryGet(ctx.User)?.ToString() ?? "anon";
                using var activity = AppTelemetry.Activity.StartActivity("mcp.resource");
                activity?.SetTag("mcp.uri", ctx.Params.Uri);
                activity?.SetTag("user.id", userId);
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
                    logger?.LogInformation("MCP resource {Uri} user {User} took {Ms}ms",
                        ctx.Params.Uri, userId, sw.ElapsedMilliseconds);
                }
            });
#pragma warning restore S2139
        });

        return services;
    }

    private static async ValueTask<CompleteResult> HandleCompleteAsync(
        RequestContext<CompleteRequestParams> ctx, CancellationToken ct)
    {
        // Tool arguments do not support completions in the spec, only prompt
        // arguments and resource template parameters.
        if (ctx.Params.Ref is PromptReference pr
            && (string.Equals(pr.Name, "plan_day", StringComparison.Ordinal)
                || string.Equals(pr.Name, "review_week", StringComparison.Ordinal))
            && string.Equals(ctx.Params.Argument?.Name, "tag", StringComparison.Ordinal))
        {
            var partial = ctx.Params.Argument?.Value ?? "";
            var tags = await GetTagsAsync(ctx, ct);
            var values = tags
                .Where(t => t.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                .Take(20)
                .ToList();
            return Complete(values);
        }

        if (ctx.Params.Ref is PromptReference coach
            && string.Equals(coach.Name, "habit_coach", StringComparison.Ordinal)
            && string.Equals(ctx.Params.Argument?.Name, "habitId", StringComparison.Ordinal))
        {
            var partial = ctx.Params.Argument?.Value ?? "";
            var ids = await GetHabitIdsAsync(ctx, partial, ct);
            return Complete(ids);
        }

        if (ctx.Params.Ref is ResourceTemplateReference rr
            && (rr.Uri ?? "").StartsWith("board://item/", StringComparison.Ordinal)
            && string.Equals(ctx.Params.Argument?.Name, "id", StringComparison.Ordinal))
        {
            var partial = ctx.Params.Argument?.Value ?? "";
            var ids = await GetItemIdsAsync(ctx, partial, ct);
            return Complete(ids);
        }

        return Complete([]);
    }

    private static CompleteResult Complete(IReadOnlyList<string> values) => new()
    {
        Completion = new Completion { Values = [.. values], Total = values.Count, HasMore = false },
    };

    private static async Task<string[]> GetTagsAsync(RequestContext<CompleteRequestParams> ctx, CancellationToken ct)
    {
        var userId = AuthenticatedUserId.TryGet(ctx.User);
        if (!userId.HasValue || ctx.Services is null)
        {
            return [];
        }

        var boards = (BoardPersistenceService?)ctx.Services.GetService(typeof(BoardPersistenceService));
        return boards is null ? [] : await boards.GetDistinctTagsAsync(userId.Value, ct);
    }

    private static async Task<IReadOnlyList<string>> GetHabitIdsAsync(
        RequestContext<CompleteRequestParams> ctx, string partial, CancellationToken ct)
    {
        var userId = AuthenticatedUserId.TryGet(ctx.User);
        if (!userId.HasValue || ctx.Services is null)
        {
            return [];
        }

        var boards = (BoardPersistenceService?)ctx.Services.GetService(typeof(BoardPersistenceService));
        if (boards is null)
        {
            return [];
        }

        var habits = await boards.SearchItemsAsync(userId.Value, null, null, App.Shared.RCL.Models.BoardSection.Habit, 50, ct);
        return habits
            .Where(h => h.Id.ToString("D").StartsWith(partial, StringComparison.OrdinalIgnoreCase)
                || h.Title.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Id.ToString("D"))
            .Take(20)
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> GetItemIdsAsync(
        RequestContext<CompleteRequestParams> ctx, string partial, CancellationToken ct)
    {
        var userId = AuthenticatedUserId.TryGet(ctx.User);
        if (!userId.HasValue || ctx.Services is null)
        {
            return [];
        }

        var boards = (BoardPersistenceService?)ctx.Services.GetService(typeof(BoardPersistenceService));
        if (boards is null)
        {
            return [];
        }

        var items = await boards.SearchItemsAsync(userId.Value, null, null, null, 50, ct);
        return items
            .Where(i => i.Id.ToString("D").StartsWith(partial, StringComparison.OrdinalIgnoreCase)
                || i.Title.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Id.ToString("D"))
            .Take(20)
            .ToList();
    }

    public static IEndpointRouteBuilder MapHabitinatorMcp(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp("/mcp")
            .DisableAntiforgery()
            .RequireAuthorization("BoardOrJwt")
            .RequireRateLimiting("mcp");
        return endpoints;
    }

    private static ILogger? GetLogger(IServiceProvider? services)
    {
        var factory = services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        return factory?.CreateLogger("Habitinator.Mcp");
    }
}
