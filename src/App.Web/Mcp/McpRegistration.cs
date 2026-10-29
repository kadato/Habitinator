using App.Web.Auth;
using App.Web.Services;

using ModelContextProtocol;
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
                "Habitinator board helper. Authenticate with a JWT bearer token, auth cookie, or MCP personal token. " +
                "Tokens can be scoped to full, tracking_only, or read_only. " +
                "Quick overview is available via get_board_summary or resources board://summary and board://summary.md. " +
                "Items can be targeted by itemId GUID or by title on toggle_item, habit_plus, habit_minus, get_item, archive_item, and daily completion. " +
                "get_snapshot and search_items accept format='markdown' for compact reading, and search_items accepts includeArchived=true. " +
                "Create items individually with create_item or in bulk with batch_create_items. " +
                "Habits move with habit_plus and habit_minus. Past dailies complete with daily_complete_for_date using 'yesterday' or yyyy-MM-dd. " +
                "Today toggles with toggle_item or batch_toggle_items. Bulk archive with bulk_archive and restore with bulk_unarchive. " +
                "Manage tags across items with rename_tag and delete_tag. " +
                "Item section is optional on ID-targeted mutations and auto-detected. " +
                "Past daily completion is append-only. No tool reverses a past daily completion. " +
                "Inspect timer totals with get_timer_summary and activity logs with list_activity_events. " +
                "Dates support yyyy-MM-dd and relative values 'today', 'yesterday', and 'tomorrow'. Times use HH:mm. " +
                "Fix the input and retry on InvalidParams. Reuse eventId on retry for log_activity and log_timer_session so the server logs the event once. " +
                "delete_item, delete_tag, import_data, and mass bulk_archive ask for elicitation confirmation. Older clients pass confirmToken instead.";
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
                var scope = ctx.User?.FindFirst("scope")?.Value;
                EnforceToolScope(ctx.Params.Name, scope);

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
        if (ctx.Params.Ref is PromptReference pr)
        {
            if ((string.Equals(pr.Name, "plan_day", StringComparison.Ordinal)
                    || string.Equals(pr.Name, "review_week", StringComparison.Ordinal)
                    || string.Equals(pr.Name, "daily_standup", StringComparison.Ordinal)
                    || string.Equals(pr.Name, "weekly_retro", StringComparison.Ordinal)
                    || string.Equals(pr.Name, "backlog_triage", StringComparison.Ordinal))
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

            if ((string.Equals(pr.Name, "plan_day", StringComparison.Ordinal)
                    || string.Equals(pr.Name, "daily_standup", StringComparison.Ordinal)
                    || string.Equals(pr.Name, "evening_review", StringComparison.Ordinal))
                && string.Equals(ctx.Params.Argument?.Name, "date", StringComparison.Ordinal))
            {
                var partial = ctx.Params.Argument?.Value ?? "";
                var today = await GetTodayAsync(ctx, ct);
                var dates = new List<string> { today.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) };
                dates.AddRange(Enumerable.Range(0, 7)
                    .Select(i => today.AddDays(-i).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
                var values = dates
                    .Where(d => d.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return Complete(values);
            }

            if ((string.Equals(pr.Name, "review_week", StringComparison.Ordinal)
                    || string.Equals(pr.Name, "weekly_retro", StringComparison.Ordinal))
                && string.Equals(ctx.Params.Argument?.Name, "period", StringComparison.Ordinal))
            {
                var partial = ctx.Params.Argument?.Value ?? "";
                string[] periods = ["last_7_days", "last_30_days", "this_month", "last_month"];
                return Complete(periods.Where(p => p.StartsWith(partial, StringComparison.OrdinalIgnoreCase)).ToList());
            }

            if (string.Equals(pr.Name, "habit_coach", StringComparison.Ordinal)
                && string.Equals(ctx.Params.Argument?.Name, "habitId", StringComparison.Ordinal))
            {
                var partial = ctx.Params.Argument?.Value ?? "";
                var ids = await GetHabitIdsAsync(ctx, partial, ct);
                return Complete(ids);
            }

            if (string.Equals(pr.Name, "break_down_todo", StringComparison.Ordinal)
                && string.Equals(ctx.Params.Argument?.Name, "todoId", StringComparison.Ordinal))
            {
                var partial = ctx.Params.Argument?.Value ?? "";
                var ids = await GetTodoIdsAsync(ctx, partial, ct);
                return Complete(ids);
            }

            if (string.Equals(pr.Name, "streak_recovery", StringComparison.Ordinal)
                && string.Equals(ctx.Params.Argument?.Name, "dailyId", StringComparison.Ordinal))
            {
                var partial = ctx.Params.Argument?.Value ?? "";
                var ids = await GetDailyIdsAsync(ctx, partial, ct);
                return Complete(ids);
            }
        }

        if (ctx.Params.Ref is ResourceTemplateReference rr)
        {
            if ((rr.Uri ?? "").StartsWith("board://item/", StringComparison.Ordinal)
                && string.Equals(ctx.Params.Argument?.Name, "id", StringComparison.Ordinal))
            {
                var partial = ctx.Params.Argument?.Value ?? "";
                var ids = await GetItemIdsAsync(ctx, partial, ct);
                return Complete(ids);
            }

            if ((rr.Uri ?? "").StartsWith("activity://day/", StringComparison.Ordinal)
                && string.Equals(ctx.Params.Argument?.Name, "date", StringComparison.Ordinal))
            {
                var partial = ctx.Params.Argument?.Value ?? "";
                var today = await GetTodayAsync(ctx, ct);
                var recentDates = Enumerable.Range(0, 7)
                    .Select(i => today.AddDays(-i).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                    .Where(d => d.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return Complete(recentDates);
            }
        }

        return Complete([]);
    }

    private static CompleteResult Complete(IReadOnlyList<string> values) => new()
    {
        Completion = new Completion { Values = [.. values], Total = values.Count, HasMore = false },
    };

    private static async Task<DateOnly> GetTodayAsync(RequestContext<CompleteRequestParams> ctx, CancellationToken ct)
    {
        var userId = AuthenticatedUserId.TryGet(ctx.User);
        if (!userId.HasValue || ctx.Services is null)
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }

        var boards = (BoardPersistenceService?)ctx.Services.GetService(typeof(BoardPersistenceService));
        return boards is null ? DateOnly.FromDateTime(DateTime.UtcNow) : await boards.TodayAsync(userId.Value, ct);
    }

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

        var habits = await boards.SearchItemsAsync(userId.Value, null, null, App.Shared.RCL.Models.BoardSection.Habit, 50, false, ct);
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

        var items = await boards.SearchItemsAsync(userId.Value, null, null, null, 50, false, ct);
        return items
            .Where(i => i.Id.ToString("D").StartsWith(partial, StringComparison.OrdinalIgnoreCase)
                || i.Title.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Id.ToString("D"))
            .Take(20)
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> GetTodoIdsAsync(
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

        var todos = await boards.SearchItemsAsync(userId.Value, null, null, App.Shared.RCL.Models.BoardSection.Todo, 50, false, ct);
        return todos
            .Where(t => t.Id.ToString("D").StartsWith(partial, StringComparison.OrdinalIgnoreCase)
                || t.Title.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Id.ToString("D"))
            .Take(20)
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> GetDailyIdsAsync(
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

        var dailies = await boards.SearchItemsAsync(userId.Value, null, null, App.Shared.RCL.Models.BoardSection.Daily, 50, false, ct);
        return dailies
            .Where(d => d.Id.ToString("D").StartsWith(partial, StringComparison.OrdinalIgnoreCase)
                || d.Title.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Id.ToString("D"))
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

    private static void EnforceToolScope(string toolName, string? scope)
    {
        var normalized = App.Shared.RCL.Models.McpTokenScopes.Normalize(scope);
        if (normalized == App.Shared.RCL.Models.McpTokenScopes.Full)
        {
            return;
        }

        var isRead = IsReadOnlyTool(toolName);
        if (normalized == App.Shared.RCL.Models.McpTokenScopes.ReadOnly)
        {
            if (!isRead)
            {
                throw new McpProtocolException(
                    $"Token scope 'read_only' permits read-only tools and resources only. Tool '{toolName}' was blocked.",
                    McpErrorCode.InvalidRequest);
            }

            return;
        }

        if (normalized == App.Shared.RCL.Models.McpTokenScopes.TrackingOnly)
        {
            if (isRead || IsTrackingTool(toolName))
            {
                return;
            }

            throw new McpProtocolException(
                $"Token scope 'tracking_only' permits tracking tools such as toggle_item, habit_plus, habit_minus, daily_complete_for_date, log_timer_session, and read-only tools only. Tool '{toolName}' was blocked.",
                McpErrorCode.InvalidRequest);
        }
    }

    private static bool IsReadOnlyTool(string name) =>
        name.StartsWith("get_", StringComparison.Ordinal)
        || name.StartsWith("list_", StringComparison.Ordinal)
        || name.StartsWith("search_", StringComparison.Ordinal)
        || string.Equals(name, "export_data", StringComparison.Ordinal);

    private static bool IsTrackingTool(string name) =>
        string.Equals(name, "toggle_item", StringComparison.Ordinal)
        || string.Equals(name, "batch_toggle_items", StringComparison.Ordinal)
        || string.Equals(name, "habit_plus", StringComparison.Ordinal)
        || string.Equals(name, "habit_minus", StringComparison.Ordinal)
        || string.Equals(name, "daily_complete_for_date", StringComparison.Ordinal)
        || string.Equals(name, "daily_skip_for_date", StringComparison.Ordinal)
        || string.Equals(name, "log_timer_session", StringComparison.Ordinal)
        || string.Equals(name, "log_activity", StringComparison.Ordinal)
        || string.Equals(name, "set_checklist_item_done", StringComparison.Ordinal);

    private static ILogger? GetLogger(IServiceProvider? services)
    {
        var factory = services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        return factory?.CreateLogger("Habitinator.Mcp");
    }
}
